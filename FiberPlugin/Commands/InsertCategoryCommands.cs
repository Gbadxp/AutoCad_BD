using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Inserção por grupo de blocos do BLOCOS.dwg: itens elétricos; a amarração usa a inserção manual daqui quando
    /// não há poste selecionado. (Postes, CTO e CEO têm comandos próprios, com numeração e identificação; a
    /// amarração automática fica no AnchoringCommand.)
    /// </summary>
    public class InsertCategoryCommands
    {
        [CommandMethod("FIBRA_INSERIR_ELETRICOS")]
        public void InsertElectrical() => InsertFromCategory(BlockCategories.Electrical, "itens elétricos", "FIBRA_INSERIR_ELETRICOS", askRotation: false, fillCoordinates: true);

        /// <param name="fillCoordinates">Preenche os atributos COORDENADA_X/Y e ZONA, se o bloco tiver.</param>
        /// <param name="drawingScale">Bloco na escala do desenho (1 em 1:1000), como a amarração automática; senão, tamanho 1.</param>
        internal static void InsertFromCategory(string category, string what, string command, bool askRotation, bool fillCoordinates,
            bool drawingScale = false)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            List<BlockEntry> blocks = BlockCategories.Blocks(category);
            if (blocks.Count == 0)
            {
                BlockInsertHelpers.ReportMissing(ed, what);
                return;
            }

            string title = "Inserir " + what.Substring(0, 1).ToUpperInvariant() + what.Substring(1);
            string? blockName = BlockInsertHelpers.ChooseBlock(blocks, title, command);
            if (blockName == null) return;
            ObjectId blockId = BlockInsertHelpers.LoadBlock(ed, db, blockName);
            if (blockId.IsNull) return;

            UtmSettings? utm = UtmZone.Get(db); // Para atributos COORDENADA_X/Y e ZONA, se o bloco tiver
            double scale = drawingScale ? DrawingScale.Factor(db) : 1.0;
            int inserted = 0;

            while (true)
            {
                PromptPointOptions ppo = blocks.Count > 1
                    ? new PromptPointOptions($"\n{blockName}: ponto de inserção [Bloco]: ", "Bloco")
                    : new PromptPointOptions($"\n{blockName}: ponto de inserção: ");
                ppo.AllowNone = true;

                PromptPointResult res = ed.GetPoint(ppo);
                if (res.Status == PromptStatus.Cancel || res.Status == PromptStatus.None) break;

                if (res.Status == PromptStatus.Keyword)
                {
                    string? other = BlockInsertHelpers.ChooseBlock(blocks, title, command);
                    if (other != null)
                    {
                        ObjectId otherId = BlockInsertHelpers.LoadBlock(ed, db, other);
                        if (!otherId.IsNull) { blockName = other; blockId = otherId; }
                    }
                    continue;
                }
                if (res.Status != PromptStatus.OK) continue;

                Point3d point = res.Value.TransformBy(ed.CurrentUserCoordinateSystem);

                // Amarração: o bloco gira acompanhando o mouse até o clique
                double rotation = 0;
                if (askRotation)
                {
                    double? angle = BlockInsertHelpers.DragRotation(ed, blockId, point,
                        "\nGire a amarração com o mouse e clique (ou digite o ângulo) <0>: ", scale);
                    if (angle == null) break;
                    rotation = angle.Value;
                }
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                    CadHelpers.InsertBlock(tr, space, blockId, point, rotation, null,
                        tag => fillCoordinates ? CadHelpers.CoordinateAttribute(tag, point, utm) : null, scale);
                    tr.Commit();
                }

                inserted++;
                ed.WriteMessage($"\n[OK]: {blockName} inserido.");
            }

            if (inserted > 0) ed.WriteMessage($"\n[INFO]: {inserted} bloco(s) inserido(s).");
        }
    }
}
