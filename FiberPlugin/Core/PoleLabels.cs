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

        /// <summary>Cria o texto do poste ou, se ele já existir, atualiza o conteúdo sem mudar de lugar.</summary>
        public static void Place(Transaction tr, Database db, BlockTableRecord space, BlockReference pole, PoleData data)
        {
            PlaceText(tr, db, space, pole, Text(data, pole.Position, UtmZone.Get(db)), Layer, below: false);
        }

        /// <summary>Cria ou atualiza o texto da CTO/CEO, centralizado logo abaixo do símbolo.</summary>
        public static void PlaceBox(Transaction tr, Database db, BlockTableRecord space, BlockReference box, BoxData data)
        {
            PlaceText(tr, db, space, box, BoxText(data), BoxLayer, below: true);
        }

        /// <param name="below">True: centralizado abaixo do bloco (CTO/CEO). False: à direita, no alto (postes).</param>
        private static void PlaceText(Transaction tr, Database db, BlockTableRecord space, BlockReference owner, string contents,
            string layer, bool below)
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
            if (found) return;

            CadHelpers.EnsureLayer(tr, db, layer, 7);

            // Posição pelo desenho do bloco (e não pelo ponto base, que nas CTO/CEO fica no canto)
            double gap = Gap * DrawingScale.Factor(db);
            Extents3d e;
            try
            {
                e = owner.GeometricExtents;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                double offset = Offset * DrawingScale.Factor(db);
                e = new Extents3d(owner.Position - new Vector3d(offset, offset, 0), owner.Position + new Vector3d(offset, offset, 0));
            }

            Point3d anchor = below
                ? new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2.0, e.MinPoint.Y - gap, 0)
                : new Point3d(e.MaxPoint.X + gap, e.MaxPoint.Y, 0);
            MText label = CadHelpers.AddText(tr, space, anchor, contents, 0,
                below ? AttachmentPoint.TopCenter : AttachmentPoint.BottomLeft, layer);
            XDataTags.TagPoleLabel(tr, db, label, handle);
        }
    }
}
