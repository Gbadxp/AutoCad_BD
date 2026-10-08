using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>Amarração dos cabos nos postes.</summary>
    public class AnchoringCommand
    {
        private const double Gap = 0.5; // m entre a borda do poste e a amarração, na escala 1:1000

        /// <summary>
        /// Amarra os postes selecionados: uma amarração em cada direção de cabo que sai do poste, sobre a linha do
        /// cabo, com a parte redonda logo depois da borda do poste e a parte aberta apontando para o cabo. Passagem = uma de cada lado, derivação = uma por linha, fim de rede =
        /// só uma. As amarrações que já estavam em volta do poste são trocadas. Enter sem selecionar insere uma
        /// amarração à mão (ponto e direção), para os casos sem cabo desenhado. Também funciona selecionando os
        /// postes antes de clicar no botão.
        /// </summary>
        [CommandMethod("FIBRA_INSERIR_AMARRACAO", CommandFlags.UsePickSet)]
        public void Insert()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            List<BlockEntry> blocks = BlockCategories.Blocks(BlockCategories.Anchoring);
            if (blocks.Count == 0)
            {
                BlockInsertHelpers.ReportMissing(ed, "amarração");
                return;
            }

            // Sem filtro na seleção: Enter sem selecionar nada é o modo manual, e uma seleção sem poste
            // (ex.: só os cabos) cai no aviso "Nenhum poste na seleção" lá embaixo, em vez de virar modo manual
            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelecione os postes a amarrar (Enter = inserir uma amarração à mão): " };
            PromptSelectionResult selection = ed.GetSelection(pso);
            if (selection.Status == PromptStatus.Cancel) return;
            if (selection.Status != PromptStatus.OK)
            {
                InsertCategoryCommands.InsertFromCategory(BlockCategories.Anchoring, "amarração", "FIBRA_INSERIR_AMARRACAO",
                    askRotation: true, fillCoordinates: false, drawingScale: true);
                return;
            }

            string? blockName = BlockInsertHelpers.ChooseBlock(blocks, "Inserir Amarração", "FIBRA_INSERIR_AMARRACAO");
            if (blockName == null) return;
            ObjectId blockId = BlockInsertHelpers.LoadBlock(ed, db, blockName);
            if (blockId.IsNull) return;

            AnchoringResult result;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                result = Anchor(tr, space, blockId, new HashSet<ObjectId>(selection.Value.GetObjectIds()), DrawingScale.Factor(db));
                tr.Commit();
            }

            if (result.Poles == 0 && result.WithoutCable.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum poste na seleção.");
                return;
            }
            if (result.Poles > 0)
            {
                ed.WriteMessage($"\n[SUCESSO]: {result.Inserted} amarração(ões) em {result.Poles} poste(s)" +
                                (result.Replaced > 0 ? $" ({result.Replaced} que já estavam em volta deles foram trocadas)." : "."));
            }
            if (result.WithoutCable.Count > 0)
            {
                ed.WriteMessage($"\n[AVISO]: Sem cabo chegando (vértice a até {FiberSettings.PoleMatchTolerance:0.#} m do poste): " +
                                string.Join(", ", result.WithoutCable) + ". Lance o cabo passando por eles ou use Enter para inserir à mão.");
            }
        }

        /// <summary>Quantos postes foram amarrados, quantas amarrações entraram e saíram, e os postes sem cabo.</summary>
        public sealed class AnchoringResult
        {
            public int Poles { get; set; }
            public int Inserted { get; set; }
            public int Replaced { get; set; }
            public List<string> WithoutCable { get; } = new List<string>();
        }

        /// <summary>
        /// Amarra os postes de <paramref name="selected"/> (os outros objetos são ignorados) com o bloco
        /// <paramref name="blockId"/>, trocando as amarrações que já estavam em volta deles. Não fala com o usuário.
        /// </summary>
        /// <param name="scale">Escala do desenho (1 em 1:1000): tamanho do bloco e folga até o poste.</param>
        public static AnchoringResult Anchor(Transaction tr, BlockTableRecord space, ObjectId blockId, ISet<ObjectId> selected, double scale)
        {
            var result = new AnchoringResult();

            // A parte redonda (em volta do ponto base) fica do lado do poste: o quanto ela avança para trás (-X)
            // define a distância até a borda do poste. O comprimento todo serve para achar as amarrações antigas.
            Extents3d? symbol = BlockInsertHelpers.DefinitionExtents(tr, blockId);
            double reach = Math.Max(0, -(symbol?.MinPoint.X ?? 0)) * scale;
            double length = symbol is Extents3d e ? (e.MaxPoint.X - e.MinPoint.X) * scale : 0;

            List<PoleInfo> poles = Poles.Collect(tr, space);
            var cables = new List<IList<(double X, double Y)>>();
            var existing = new List<(ObjectId Id, ObjectId Pole)>(); // Amarração já desenhada e o poste mais perto dela
            foreach (ObjectId id in space)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Polyline poly && XDataTags.GetCableName(poly) != null)
                {
                    cables.Add(Enumerable.Range(0, poly.NumberOfVertices).Select(i => { Point2d p = poly.GetPoint2dAt(i); return (p.X, p.Y); }).ToList());
                }
                else if (obj is BlockReference br && BlockCategories.Of(CadHelpers.GetBlockName(tr, br)) == BlockCategories.Anchoring)
                {
                    existing.Add((id, Poles.Nearest(poles, br.Position, double.MaxValue)?.Id ?? ObjectId.Null));
                }
            }

            foreach (PoleInfo pole in poles.Where(p => selected.Contains(p.Id)))
            {
                Extents3d box = BlockInsertHelpers.SymbolBox(tr, (BlockReference)tr.GetObject(pole.Id, OpenMode.ForRead));
                List<AnchoringPlacement> placements = Anchorings.Place(cables, (pole.Position.X, pole.Position.Y),
                    ((box.MinPoint.X, box.MinPoint.Y), (box.MaxPoint.X, box.MaxPoint.Y)),
                    FiberSettings.PoleMatchTolerance, reach, Gap * scale);
                if (placements.Count == 0)
                {
                    result.WithoutCable.Add(pole.Number);
                    continue;
                }

                // Refaz as amarrações deste poste: apaga as que já estavam em volta dele (inclusive as viradas ao
                // contrário), só as que têm este poste como o mais perto, para não levar as de um poste vizinho
                double radius = placements.Max(p => Math.Sqrt(Math.Pow(p.X - pole.Position.X, 2) + Math.Pow(p.Y - pole.Position.Y, 2))) + length;
                foreach (var (id, nearestPole) in existing)
                {
                    if (nearestPole != pole.Id) continue;
                    var old = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    if (old.IsErased || old.Position.DistanceTo(pole.Position) > radius) continue;
                    old.UpgradeOpen();
                    old.Erase();
                    result.Replaced++;
                }

                foreach (AnchoringPlacement p in placements)
                {
                    CadHelpers.InsertBlock(tr, space, blockId, new Point3d(p.X, p.Y, pole.Position.Z), p.Rotation, null, scale: scale);
                    result.Inserted++;
                }
                result.Poles++;
            }
            return result;
        }
    }
}
