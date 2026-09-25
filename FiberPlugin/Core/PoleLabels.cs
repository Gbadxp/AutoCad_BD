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
    ///   E 345678.12
    ///   N 8712345.67
    /// O tipo (DT/CC) não aparece no desenho: ele só entra na listagem de postes.
    /// </summary>
    public static class PoleLabels
    {
        public const string Layer = "FIBRA_POSTES_ID";

        // Quebra de linha do MText
        private const string LineBreak = @"\P";

        // Afastamento do texto em relação ao ponto de inserção do bloco, na escala 1:1000
        private const double Offset = 1.5;

        public static string Text(PoleData data, Point3d position)
        {
            return PoleData.NumberText(data.Number) + LineBreak +
                   data.HeightEffort + LineBreak +
                   "E " + position.X.ToString("F2", CultureInfo.InvariantCulture) + LineBreak +
                   "N " + position.Y.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>Cria (ou recria) o texto do poste, apagando o texto anterior do mesmo bloco.</summary>
        public static MText Place(Transaction tr, Database db, BlockTableRecord space, BlockReference pole, PoleData data)
        {
            string handle = pole.Handle.ToString();

            // Remove o texto anterior deste poste (coleta antes de apagar)
            var previous = new List<ObjectId>();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is MText txt && XDataTags.GetPoleLabelOwner(txt) == handle) previous.Add(id);
            }
            foreach (ObjectId id in previous) tr.GetObject(id, OpenMode.ForWrite).Erase();

            CadHelpers.EnsureLayer(tr, db, Layer, 7);

            double offset = Offset * DrawingScale.Factor(db);
            MText label = CadHelpers.AddText(tr, space,
                new Point3d(pole.Position.X + offset, pole.Position.Y + offset, 0),
                Text(data, pole.Position), 0, AttachmentPoint.BottomLeft, Layer);

            XDataTags.TagPoleLabel(tr, db, label, handle);
            return label;
        }
    }
}
