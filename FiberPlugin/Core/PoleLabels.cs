using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Texto de identificação do poste, ao lado do bloco:
    ///   P-12
    ///   11/300
    ///   20 L
    ///   405110.92 m E
    ///   9032585.41 m S
    /// O tipo (DT/CC) não aparece no desenho: ele só entra na listagem de postes.
    /// </summary>
    public static class PoleLabels
    {
        public const string Layer = "FIBRA_POSTES_ID";
        public const string BoxLayer = "FIBRA_CAIXAS_ID";   // Textos das CTO e CEO

        // Quebra de linha do MText
        private const string LineBreak = @"\P";

        // Afastamento do texto em relação ao ponto de inserção do bloco, na escala 1:1000
        private const double Offset = 1.5;

        // Espaço entre o desenho do bloco e o texto, na escala 1:1000
        private const double Gap = 0.5;

        /// <param name="utm">Zona do projeto; sem ela a linha "20 L" é omitida e o norte sai como hemisfério sul.</param>
        public static string Text(PoleData data, Point3d position, UtmSettings? utm)
        {
            return PoleData.NumberText(data.Number) + LineBreak +
                   data.HeightEffort + LineBreak +
                   CoordinateLines(position, utm);
        }

        /// <summary>Texto da CTO/CEO: só a identificação (CTO-01), sem coordenadas.</summary>
        public static string BoxText(BoxData data) => data.Id;

        /// <summary>"20 L", "405110.92 m E" e "9032585.41 m S", uma por linha.</summary>
        private static string CoordinateLines(Point3d position, UtmSettings? utm)
        {
            string zone = utm != null ? UtmZone.ZoneText(position, utm) + LineBreak : "";
            return zone +
                   UtmZone.EastingText(position.X) + LineBreak +
                   UtmZone.NorthingText(position.Y, utm?.South ?? true);
        }

        /// <summary>Cria (ou recria) o texto do poste, apagando o texto anterior do mesmo bloco.</summary>
        public static MText Place(Transaction tr, Database db, BlockTableRecord space, BlockReference pole, PoleData data)
        {
            return PlaceText(tr, db, space, pole, Text(data, pole.Position, UtmZone.Get(db)), Layer);
        }

        /// <summary>Cria (ou recria) o texto da CTO/CEO.</summary>
        public static MText PlaceBox(Transaction tr, Database db, BlockTableRecord space, BlockReference box, BoxData data)
        {
            return PlaceText(tr, db, space, box, BoxText(data), BoxLayer);
        }

        /// <summary>
        /// Atualiza o texto do poste (renumeração) mantendo a posição em que o usuário o deixou.
        /// Se o poste não tiver texto, cria um novo.
        /// </summary>
        public static void RefreshPole(Transaction tr, Database db, BlockTableRecord space, BlockReference pole, PoleData data)
        {
            Refresh(tr, db, space, pole, Text(data, pole.Position, UtmZone.Get(db)), Layer);
        }

        /// <summary>Atualiza o texto da CTO/CEO mantendo a posição; cria se não houver.</summary>
        public static void RefreshBox(Transaction tr, Database db, BlockTableRecord space, BlockReference box, BoxData data)
        {
            Refresh(tr, db, space, box, BoxText(data), BoxLayer);
        }

        private static void Refresh(Transaction tr, Database db, BlockTableRecord space, BlockReference owner, string contents, string layer)
        {
            string handle = owner.Handle.ToString();
            bool found = false;
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not MText txt || XDataTags.GetPoleLabelOwner(txt) != handle) continue;
                txt.UpgradeOpen();
                txt.Contents = contents;
                found = true;
            }
            if (!found) PlaceText(tr, db, space, owner, contents, layer);
        }

        /// <summary>Texto de identificação ao lado do bloco, substituindo o texto anterior do mesmo bloco.</summary>
        private static MText PlaceText(Transaction tr, Database db, BlockTableRecord space, BlockReference owner, string contents, string layer)
        {
            string handle = owner.Handle.ToString();

            // Remove o texto anterior deste poste (coleta antes de apagar)
            var previous = new List<ObjectId>();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is MText txt && XDataTags.GetPoleLabelOwner(txt) == handle) previous.Add(id);
            }
            foreach (ObjectId id in previous) tr.GetObject(id, OpenMode.ForWrite).Erase();

            CadHelpers.EnsureLayer(tr, db, layer, 7);

            // Canto superior direito do desenho do bloco: funciona para postes (ponto base no centro)
            // e para símbolos com o ponto base no canto, como as CTO/CEO
            double gap = Gap * DrawingScale.Factor(db);
            Point3d anchor;
            try
            {
                Extents3d e = owner.GeometricExtents;
                anchor = new Point3d(e.MaxPoint.X + gap, e.MaxPoint.Y, 0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                double offset = Offset * DrawingScale.Factor(db);
                anchor = new Point3d(owner.Position.X + offset, owner.Position.Y + offset, 0);
            }

            MText label = CadHelpers.AddText(tr, space, anchor, contents, 0, AttachmentPoint.BottomLeft, layer);

            XDataTags.TagPoleLabel(tr, db, label, handle);
            return label;
        }
    }
}
