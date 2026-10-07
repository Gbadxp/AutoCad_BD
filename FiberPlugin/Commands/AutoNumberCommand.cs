using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class AutoNumberCommand
    {
        /// <summary>
        /// Numera os postes sozinho: clica-se no primeiro e a numeração segue os cabos (o mesmo cabo até o fim, depois as
        /// derivações); postes sem cabo entram no fim, pela proximidade. As CTO e CEO podem ser renumeradas junto, na
        /// ordem dos postes em que estão. Em todos os postes do desenho ou só nos selecionados (os números dos outros
        /// são pulados). Desfaz com U, como qualquer comando.
        /// </summary>
        [CommandMethod("FIBRA_NUMERAR_AUTO")]
        public void AutoNumber()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Quais postes
            string? scope = CadHelpers.AskKeyword(ed, "\nNumeração automática dos postes [Todos/Selecionar] <Todos>: ", "Todos Selecionar", "Todos");
            if (scope == null) return;
            HashSet<ObjectId>? selected = null;
            if (scope == "Selecionar")
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                PromptSelectionResult psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecione os postes a numerar: " }, filter);
                if (psr.Status != PromptStatus.OK) return;
                selected = new HashSet<ObjectId>(psr.Value.GetObjectIds());
            }

            List<PoleInfo> all, poles;
            List<BoxInfo> boxes;
            var cables = new List<List<Point3d>>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                all = Poles.Collect(tr, modelSpace).Where(p => p.Data != null).ToList();
                boxes = Boxes.Collect(tr, modelSpace);
                foreach (ObjectId id in modelSpace)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Polyline poly && XDataTags.GetCableName(poly) != null)
                        cables.Add(Enumerable.Range(0, poly.NumberOfVertices).Select(poly.GetPoint3dAt).ToList());
                }
                tr.Commit();
            }
            poles = selected == null ? all : all.Where(p => selected.Contains(p.Id)).ToList();
            if (poles.Count == 0)
            {
                ed.WriteMessage(selected == null
                    ? "\n[AVISO]: Nenhum poste inserido pelo plugin no desenho (Inserir Postes)."
                    : "\n[AVISO]: Nenhum poste do plugin na seleção.");
                return;
            }

            // 2. Primeiro poste: o mais perto do clique (pode clicar no texto dele)
            PoleInfo? first = null;
            while (first == null)
            {
                PromptPointResult ppr = ed.GetPoint("\nClique no primeiro poste da numeração: ");
                if (ppr.Status != PromptStatus.OK) return;
                first = Poles.Nearest(poles, ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem), FiberSettings.PoleLinkRadius);
                if (first == null) ed.WriteMessage($"\n[AVISO]: Nenhum poste a numerar a até {FiberSettings.PoleLinkRadius:F0} m do clique.");
            }

            // 3. Número do primeiro (os números dos postes que ficam de fora são pulados)
            var others = new HashSet<int>(all.Where(p => !poles.Contains(p)).Select(p => p.Data!.Number));
            int? start = Poles.AskNumber(ed, Poles.NextFree(others, selected == null ? 1 : poles.Min(p => p.Data!.Number)), "do primeiro poste");
            if (start == null) return;

            // 4. CTO e CEO dos postes numerados
            // Poste gravado na caixa; se ele estiver longe dela (desenho copiado: o handle é do poste original), o mais perto
            var poleByHandle = all.ToDictionary(p => p.Id.Handle.ToString());
            PoleInfo? PoleOf(BoxInfo box) =>
                box.Data.PoleHandle.Length > 0 && poleByHandle.TryGetValue(box.Data.PoleHandle, out PoleInfo? p) &&
                p.Position.DistanceTo(box.Position) <= FiberSettings.PoleLinkRadius
                    ? p
                    : Poles.Nearest(all, box.Position, FiberSettings.PoleLinkRadius);
            List<(BoxInfo Box, PoleInfo Pole)> ownBoxes = boxes
                .Select(b => (Box: b, Pole: PoleOf(b)))
                .Where(x => x.Pole != null && poles.Contains(x.Pole))
                .Select(x => (x.Box, x.Pole!))
                .ToList();
            bool renumberBoxes = ownBoxes.Count > 0 &&
                CadHelpers.AskYes(ed, $"\nNumerar também as {ownBoxes.Count} CTO/CEO desses postes, na ordem deles? [Sim/Nao] <Sim>: ");

            // 5. Ordem pelos cabos e números novos (os postes fora da seleção só dão passagem ao cabo)
            var (order, withoutCable) = AutoNumbering.Order(poles.Select(p => p.Position).ToList(), poles.IndexOf(first), cables,
                FiberSettings.PoleLinkRadius, all.Where(p => !poles.Contains(p)).Select(p => p.Position).ToList());
            var newNumber = new Dictionary<PoleInfo, int>();
            var position = new Dictionary<PoleInfo, int>();
            int number = start.Value;
            foreach (int index in order)
            {
                number = Poles.NextFree(others, number);
                newNumber[poles[index]] = number++;
                position[poles[index]] = position.Count;
            }

            var newBoxNumber = new Dictionary<BoxInfo, int>();
            if (renumberBoxes)
            {
                foreach (var kind in ownBoxes.GroupBy(b => b.Box.Data.Kind))
                {
                    var used = new HashSet<int>(boxes.Where(b => b.Data.Kind == kind.Key && !kind.Any(k => k.Box == b)).Select(b => b.Data.Number));
                    int next = 1;
                    foreach (var (box, _) in kind.OrderBy(b => position[b.Pole]).ThenBy(b => b.Box.Data.Number))
                    {
                        next = Poles.NextFree(used, next);
                        newBoxNumber[box] = next++;
                    }
                }
            }

            // 6. Grava (um bloco por transação: com o texto em layer travada, o bloco também fica como estava). O texto de cada
            // bloco é levantado uma vez, já com o desenho copiado resolvido (veja PoleLabels.IndexLabels)
            Dictionary<string, List<ObjectId>> labels;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                labels = PoleLabels.IndexLabels(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                tr.Commit();
            }
            ed.WriteMessage($"\n[INFO]: Numerando {newNumber.Count} poste(s)" + (newBoxNumber.Count > 0 ? $" e {newBoxNumber.Count} CTO/CEO..." : "..."));
            int changedPoles = 0, changedBoxes = 0, locked = 0, failed = 0;
            string? firstError = null;
            void Count(string? error, ref int changed)
            {
                if (error == null) changed++;
                else if (error == LockedLayer) locked++;
                else { failed++; firstError ??= error; }
            }
            foreach (var pair in newNumber)
            {
                Count(Write(db, pair.Key.Id, (tr, br, space) => RenumberCommand.WritePoleNumber(tr, db, space, br, pair.Key.Data!, pair.Value, labels)),
                      ref changedPoles);
            }
            foreach (var pair in newBoxNumber)
            {
                Count(Write(db, pair.Key.Id, (tr, br, space) => RenumberCommand.WriteBoxNumber(tr, db, space, br, pair.Key.Data, pair.Value, labels)),
                      ref changedBoxes);
            }

            // 7. Resumo
            ed.WriteMessage($"\n[SUCESSO]: {changedPoles} poste(s) numerado(s) de {PoleData.NumberText(newNumber.Values.Min())} a {PoleData.NumberText(newNumber.Values.Max())}, " +
                            $"seguindo os cabos a partir do {PoleData.NumberText(newNumber[first])}.");
            if (withoutCable > 0)
                ed.WriteMessage($"\n[INFO]: {withoutCable} poste(s) sem cabo entraram pela proximidade, depois dos que os cabos alcançam.");
            if (others.Count > 0 && selected != null)
                ed.WriteMessage($"\n[INFO]: Os números dos {all.Count - poles.Count} poste(s) fora da seleção foram pulados.");
            foreach (var kind in newBoxNumber.GroupBy(b => b.Key.Data.Kind))
            {
                ed.WriteMessage($"\n[INFO]: {kind.Count()} {kind.Key} renumerada(s) na ordem dos postes " +
                                $"({UserSettings.Current.Name(UserSettings.Current.BoxPrefix(kind.Key), kind.Min(k => k.Value))} a " +
                                $"{UserSettings.Current.Name(UserSettings.Current.BoxPrefix(kind.Key), kind.Max(k => k.Value))}).");
            }
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} bloco(s) em layer travada ficaram com o número antigo.");
            if (failed > 0) ed.WriteMessage($"\n[ERRO]: {failed} bloco(s) não puderam ser numerados ({firstError}).");
            ed.Regen();
        }

        private const string LockedLayer = "layer travada";

        /// <summary>
        /// Abre o bloco para escrita e grava numa transação só dele. Retorna null se gravou; se não (e então nada muda no
        /// bloco), o motivo: LockedLayer quando ele ou o texto dele está em layer travada, ou a mensagem do AutoCAD.
        /// </summary>
        private static string? Write(Database db, ObjectId id, Action<Transaction, BlockReference, BlockTableRecord> write)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                    write(tr, br, (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite));
                    tr.Commit();
                }
                return null;
            }
            catch (System.Exception ex)
            {
                // Qualquer erro fica neste bloco: os outros continuam e o resumo diz quantos e por quê
                return ex is Autodesk.AutoCAD.Runtime.Exception acad && acad.ErrorStatus == ErrorStatus.OnLockedLayer ? LockedLayer : ex.Message;
            }
        }
    }
}
