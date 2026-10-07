using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class BlockSizeCommand
    {
        /// <summary>
        /// Muda o tamanho de blocos já inseridos: cada bloco selecionado fica com o maior lado igual ao tamanho
        /// digitado (ou é multiplicado pelo fator). O bloco continua no ponto em que foi inserido e o texto de
        /// identificação (poste, CTO, CEO) acompanha.
        /// </summary>
        [CommandMethod("FIBRA_TAMANHO_BLOCO", CommandFlags.UsePickSet)]
        public void ResizeBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelecione o(s) bloco(s) para mudar o tamanho: " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK) return;
            ObjectId[] ids = psr.Value.GetObjectIds();

            // Tamanho atual do primeiro bloco, sugerido na pergunta
            double current;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                current = ids.Select(id => Extents((BlockReference)tr.GetObject(id, OpenMode.ForRead)))
                    .Where(e => e != null)
                    .Select(e => Longest(e!.Value))
                    .FirstOrDefault();
            }

            string hint = current > 0 ? $" <{current:0.##}>" : "";
            var pdo = new PromptDoubleOptions($"\nNovo tamanho, maior lado em m [Fator]{hint}: ", "Fator")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult res = ed.GetDouble(pdo);

            double? size = null, factor = null;
            if (res.Status == PromptStatus.OK) size = res.Value;
            else if (res.Status == PromptStatus.Keyword)
            {
                var pfo = new PromptDoubleOptions("\nFator de escala (2 = dobro, 0.5 = metade): ") { AllowNegative = false, AllowZero = false };
                PromptDoubleResult f = ed.GetDouble(pfo);
                if (f.Status != PromptStatus.OK) return;
                factor = f.Value;
            }
            else return; // Enter mantém o tamanho; Esc cancela

            int changed = 0, locked = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    Extents3d? before = Extents(br);
                    if (before == null || Longest(before.Value) < 1e-9) continue;

                    double ratio = factor ?? size!.Value / Longest(before.Value);
                    if (Math.Abs(ratio - 1) < 1e-9) continue;

                    if (Scale(tr, br, ratio)) changed++;
                    else locked++;
                }
                tr.Commit();
            }

            ed.WriteMessage($"\n[OK]: {changed} bloco(s) com tamanho " + (size != null ? $"{size:0.##} m (maior lado)." : $"x{factor:0.##}."));
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} bloco(s) em layer travada não foram alterados.");
        }

        /// <summary>
        /// Multiplica o tamanho do bloco por <paramref name="ratio"/> sem tirá-lo do lugar, e o texto de identificação
        /// acompanha. CTO/CEO crescem em volta do centro do símbolo (o ponto base fica no canto); os demais blocos, em volta
        /// do ponto de inserção, que guarda a coordenada do poste/item. False se o bloco está em layer travada.
        /// </summary>
        /// <param name="labels">Textos já levantados (PoleLabels.IndexLabels), para mudar muitos blocos de uma vez.</param>
        internal static bool Scale(Transaction tr, BlockReference br, double ratio, Dictionary<string, List<ObjectId>>? labels = null)
        {
            Extents3d? before = Extents(br);
            if (before == null) return true;
            Point3d center = XDataTags.ReadBox(br) != null
                ? before.Value.MinPoint + (before.Value.MaxPoint - before.Value.MinPoint) / 2.0
                : br.Position;

            try
            {
                br.UpgradeOpen();
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return false; // Layer travada
            }

            br.TransformBy(Matrix3d.Scaling(ratio, center));
            PoleLabels.Follow(tr, (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForRead), br, before.Value, labels);
            return true;
        }

        private static Extents3d? Extents(BlockReference br)
        {
            try { return br.GeometricExtents; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        private static double Longest(Extents3d e) =>
            Math.Max(e.MaxPoint.X - e.MinPoint.X, e.MaxPoint.Y - e.MinPoint.Y);
    }
}
