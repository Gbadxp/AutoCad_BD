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
    /// Inserir CTO e Inserir CEO: bloco do BLOCOS.dwg com numeração automática (CTO-01, CEO-01...),
    /// texto de identificação (sem coordenadas) e vínculo com o poste mais próximo (para o relatório).
    /// </summary>
    public class InsertBoxCommands
    {
        [CommandMethod("FIBRA_INSERIR_CTO")]
        public void InsertCto() => InsertBox(BlockCategories.Cto);

        [CommandMethod("FIBRA_INSERIR_CEO")]
        public void InsertCeo() => InsertBox(BlockCategories.Ceo);

        private static void InsertBox(string kind)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            string command = "FIBRA_INSERIR_" + kind;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            List<BlockEntry> blocks = BlockCategories.Blocks(kind);
            if (blocks.Count == 0)
            {
                BlockInsertHelpers.ReportMissing(ed, kind);
                return;
            }

            string? blockName = BlockInsertHelpers.ChooseBlock(blocks, "Inserir " + kind, command);
            if (blockName == null) return;
            ObjectId blockId = BlockInsertHelpers.LoadBlock(ed, db, blockName);
            if (blockId.IsNull) return;

            // Símbolo no tamanho padrão e centrado no clique, seja qual for a unidade do bloco no BLOCOS.dwg
            double symbolSize = FiberSettings.BoxSymbolSize * DrawingScale.Factor(db, ScaleItem.BoxIcon);
            var (scale, centerOffset) = BlockInsertHelpers.FitSymbol(db, blockId, symbolSize);

            HashSet<int> used;
            List<PoleInfo> poles;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                used = new HashSet<int>(Boxes.Collect(tr, modelSpace).Where(b => b.Data.Kind == kind).Select(b => b.Data.Number));
                poles = Poles.Collect(tr, modelSpace);
                tr.Commit();
            }

            int? start = Poles.AskNumber(ed, Poles.NextFree(used, 1), "da próxima " + kind);
            if (start == null) return;
            int number = start.Value;
            int inserted = 0;

            while (true)
            {
                number = Poles.NextFree(used, number);
                var data = new BoxData { Kind = kind, Number = number };

                var ppo = new PromptPointOptions($"\n{data.Id} ({blockName}): ponto de inserção [Modelo/Numero]: ", "Modelo Numero")
                {
                    AllowNone = true
                };
                PromptPointResult res = ed.GetPoint(ppo);
                if (res.Status == PromptStatus.Cancel || res.Status == PromptStatus.None) break;

                if (res.Status == PromptStatus.Keyword)
                {
                    if (res.StringResult == "Modelo")
                    {
                        string? other = blocks.Count > 1 ? BlockInsertHelpers.ChooseBlock(blocks, "Inserir " + kind, command) : null;
                        if (other != null)
                        {
                            ObjectId otherId = BlockInsertHelpers.LoadBlock(ed, db, other);
                            if (!otherId.IsNull)
                            {
                                blockName = other;
                                blockId = otherId;
                                (scale, centerOffset) = BlockInsertHelpers.FitSymbol(db, blockId, symbolSize);
                            }
                        }
                        else if (blocks.Count == 1)
                        {
                            ed.WriteMessage($"\n[INFO]: O {BlockRepository.LibraryFileName} tem um só modelo de {kind}.");
                        }
                    }
                    else if (res.StringResult == "Numero")
                    {
                        int? n = Poles.AskNumber(ed, number, "da próxima " + kind);
                        if (n != null) number = n.Value;
                    }
                    continue;
                }
                if (res.Status != PromptStatus.OK) continue;

                Point3d point = res.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                PoleInfo? pole = Poles.Nearest(poles, point, FiberSettings.PoleLinkRadius);
                data.PoleHandle = pole?.Id.Handle.ToString() ?? "";

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                    BlockReference br = CadHelpers.InsertBlock(tr, space, blockId, point - centerOffset, 0, null, tag =>
                    {
                        // Só a identificação: CTO/CEO não levam coordenadas no desenho
                        return CadHelpers.IsTag(tag, CadHelpers.NumberTags) ? data.Id : null;
                    }, scale);

                    XDataTags.TagBox(tr, db, br, data);
                    PoleLabels.PlaceBox(tr, db, space, br, data, PoleLabels.NewBlock);
                    tr.Commit();
                }

                used.Add(number);
                inserted++;
                ed.WriteMessage($"\n[OK]: {data.Id} ({blockName}) | {(pole != null ? "poste " + pole.Number : "sem poste por perto")}");
            }

            if (inserted > 0) ed.WriteMessage($"\n[INFO]: {inserted} {kind}(s) inserida(s).");
        }
    }
}
