using System.Collections.Generic;
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
    /// Inserção por grupo de blocos do BLOCOS.dwg: itens elétricos, amarração e os demais blocos.
    /// (Postes, CTO e CEO têm comandos próprios, com numeração e identificação.)
    /// </summary>
    public class InsertCategoryCommands
    {
        [CommandMethod("FIBRA_INSERIR_ELETRICOS")]
        public void InsertElectrical() => InsertFromCategory(BlockCategories.Electrical, "itens elétricos", "FIBRA_INSERIR_ELETRICOS", askRotation: false, fillCoordinates: true);

        /// <summary>Amarração: depois do ponto, pede a direção (para onde a amarração aponta). Sem coordenadas.</summary>
        [CommandMethod("FIBRA_INSERIR_AMARRACAO")]
        public void InsertAnchoring() => InsertFromCategory(BlockCategories.Anchoring, "amarração", "FIBRA_INSERIR_AMARRACAO", askRotation: true, fillCoordinates: false);

        /// <summary>Blocos que não pertencem a nenhum grupo (para blocos novos acrescentados ao BLOCOS.dwg).</summary>
        [CommandMethod("FIBRA_INSERIR_BLOCO")]
        public void InsertOthers() => InsertFromCategory(BlockCategories.Others, "outros blocos", "FIBRA_INSERIR_BLOCO", askRotation: false, fillCoordinates: true);

        /// <param name="fillCoordinates">Preenche os atributos COORDENADA_X/Y e ZONA, se o bloco tiver.</param>
        private static void InsertFromCategory(string category, string what, string command, bool askRotation, bool fillCoordinates)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

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
                        "\nGire a amarração com o mouse e clique (ou digite o ângulo) <0>: ");
                    if (angle == null) break;
                    rotation = angle.Value;
                }
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    CadHelpers.InsertBlock(tr, space, blockId, point, rotation, null,
                        tag => fillCoordinates ? CadHelpers.CoordinateAttribute(tag, point, utm) : null);
                    tr.Commit();
                }

                inserted++;
                ed.WriteMessage($"\n[OK]: {blockName} inserido.");
            }

            if (inserted > 0) ed.WriteMessage($"\n[INFO]: {inserted} bloco(s) inserido(s).");
        }
    }
}
