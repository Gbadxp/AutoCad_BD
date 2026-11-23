using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class RegistrationCommand
    {
        private const string MarkDone = "Cadastrar", MarkPending = "Pendente", ShowPending = "Faltam", ShowDone = "Feitos";

        // Última opção e tipo usados (enquanto o AutoCAD estiver aberto)
        private static string _lastOption = MarkDone, _lastKind = "Todos";

        /// <summary>
        /// Cadastro na plataforma da Energisa:
        /// <list type="bullet">
        /// <item>Cadastrar / Pendente: seleciona uma área ou clica nos objetos (só de um tipo, se quiser: postes, CTO, CEO,
        /// cabos ou outros) e marca como cadastrados ou tira a marca. Não mostra nada no desenho: as marcas aparecem com o
        /// Ver Cadastro (se já estiverem à vista, acompanham);</item>
        /// <item>Faltam / Feitos: mostra só as marcas vermelhas (falta cadastrar) ou só as verdes (cadastrados).</item>
        /// </list>
        /// </summary>
        [CommandMethod("FIBRA_CADASTRO", CommandFlags.UsePickSet)]
        public void Register()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            string? option = CadHelpers.AskKeyword(ed,
                $"\nCadastro na Energisa [{MarkDone}/{MarkPending}/{ShowPending}/{ShowDone}] <{_lastOption}>: ",
                string.Join(" ", MarkDone, MarkPending, ShowPending, ShowDone), _lastOption);
            if (option == null) return;
            _lastOption = option;

            if (option == ShowPending || option == ShowDone)
            {
                var (shownCounts, shownKept) = RedrawMarks(db, showDone: option == ShowDone, showPending: option == ShowPending);
                WriteCounts(ed, shownCounts, shownKept, option == ShowDone ? "Mostrando só os cadastrados (verde)." : "Mostrando só os que faltam (vermelho).");
                ed.Regen();
                return;
            }

            bool done = option == MarkDone;
            string? kind = CadHelpers.AskKeyword(ed,
                $"\nQue objetos [Todos/{string.Join("/", Registration.Kinds)}] <{_lastKind}>: ",
                "Todos " + string.Join(" ", Registration.Kinds), _lastKind);
            if (kind == null) return;
            _lastKind = kind;

            var pso = new PromptSelectionOptions
            {
                MessageForAdding = $"\nSelecione a área ou os objetos a marcar como {(done ? "CADASTRADOS" : "PENDENTES")}" +
                                   (kind == "Todos" ? "" : $" (só {kind})") + ": "
            };
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            int changed = 0, locked = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in psr.Value.GetObjectIds())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent || Registration.IsMark(ent)) continue;
                    string? what = Registration.KindOf(tr, ent);
                    if (what == null || (kind != "Todos" && what != kind) || Registration.IsDone(ent) == done) continue;
                    if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                    ent.UpgradeOpen();
                    Registration.SetDone(tr, db, ent, done);
                    changed++;
                }
                tr.Commit();
            }
            ed.WriteMessage($"\n[OK]: {changed} objeto(s) marcado(s) como {(done ? "cadastrado(s)" : "pendente(s)")}.");
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} objeto(s) em layer travada ficaram como estavam.");

            // As marcas só mudam se já estiverem à vista (com as mesmas cores de antes); escondidas, continuam escondidas
            var (showingDone, showingPending) = ShownMarks(db);
            var (counts, kept) = RedrawMarks(db, showingDone, showingPending);
            WriteCounts(ed, counts, kept, showingDone || showingPending ? "" : "Para ver no desenho, clique em Ver Cadastro.");
            if (showingDone || showingPending) ed.Regen();
        }

        /// <summary>
        /// Ver Cadastro: um clique mostra as marcas (verde = cadastrado, vermelho = falta) e o clique seguinte esconde. As
        /// marcas ficam em layers que não imprimem e são desenhadas na hora, com o desenho de agora.
        /// </summary>
        [CommandMethod("FIBRA_CADASTRO_VER")]
        public void ToggleMarks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var (done, pending) = ShownMarks(db);
            bool show = !done && !pending;
            var (counts, kept) = RedrawMarks(db, show, show);
            if (show) WriteCounts(ed, counts, kept, "Clique em Ver Cadastro de novo para esconder.");
            else
            {
                ed.WriteMessage("\n[INFO]: Marcas do cadastro escondidas. Clique em Ver Cadastro para mostrar de novo.");
                if (kept > 0) ed.WriteMessage($"\n[AVISO]: {kept} marca(s) em layer travada ficaram: destrave as layers do cadastro.");
            }
            ed.Regen();
        }

        /// <summary>Quantos de cada tipo estão cadastrados, o total e as cores na tela, na linha de comando.</summary>
        private static void WriteCounts(Editor ed, Dictionary<string, (int Done, int Total)> counts, int kept, string hint)
        {
            ed.WriteMessage("\n--- CADASTRO NA ENERGISA (cadastrados / total) ---");
            foreach (string kind in Registration.Kinds)
            {
                if (counts.TryGetValue(kind, out var c) && c.Total > 0) ed.WriteMessage($"\n{kind}: {c.Done} / {c.Total}");
            }
            int done = counts.Values.Sum(c => c.Done), total = counts.Values.Sum(c => c.Total);
            ed.WriteMessage(total == 0
                ? "\n[AVISO]: Nenhum poste, CTO, CEO, cabo ou bloco do plugin no desenho."
                : $"\n[INFO]: {done} de {total} cadastrado(s), faltam {total - done}. No desenho: verde = cadastrado, vermelho = falta " +
                  $"(não imprime). {hint}".TrimEnd());
            if (kept > 0) ed.WriteMessage($"\n[AVISO]: {kept} marca(s) antiga(s) em layer travada ficaram: destrave as layers do cadastro.");
        }

        /// <summary>As marcas que estão no desenho agora: verdes (cadastrados) e vermelhas (falta).</summary>
        internal static (bool Done, bool Pending) ShownMarks(Database db)
        {
            bool done = false, pending = false;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead))
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent || !Registration.IsMark(ent)) continue;
                    if (ent.Layer.Equals(Registration.DoneLayer, StringComparison.OrdinalIgnoreCase)) done = true;
                    else pending = true;
                    if (done && pending) break;
                }
            }
            return (done, pending);
        }

        /// <summary>
        /// Apaga as marcas e desenha de novo as pedidas: verdes nos cadastrados (<paramref name="showDone"/>) e vermelhas nos
        /// que faltam (<paramref name="showPending"/>); com as duas falsas, só apaga. Retorna, por tipo, quantos estão
        /// cadastrados e o total, e quantas marcas antigas ficaram por estar em layer travada. Objeto oculto (janela
        /// Visibilidade) conta, mas fica sem marca.
        /// </summary>
        internal static (Dictionary<string, (int Done, int Total)> Counts, int KeptMarks) RedrawMarks(Database db, bool showDone, bool showPending)
        {
            var counts = new Dictionary<string, (int Done, int Total)>();
            int kept;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                var marks = new List<ObjectId>();
                var objects = new List<(Entity Ent, string Kind, bool Done)>();
                foreach (ObjectId id in space)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    if (Registration.IsMark(ent)) { marks.Add(id); continue; }
                    if (Registration.KindOf(tr, ent) is string kind) objects.Add((ent, kind, Registration.IsDone(ent)));
                }
                kept = CadHelpers.EraseUnlocked(tr, marks);

                if (showDone || showPending)
                {
                    CadHelpers.EnsureNonPlottingLayer(tr, db, Registration.DoneLayer, 3);
                    CadHelpers.EnsureNonPlottingLayer(tr, db, Registration.PendingLayer, 1);
                    TurnOn(tr, db); // Desligadas pelo Ocultar das versões 1.9.102 a 1.9.104
                }
                double scale = DrawingScale.Factor(db);
                foreach (var (ent, kind, done) in objects)
                {
                    if (ent.Visible && (done ? showDone : showPending)) Registration.DrawMark(tr, db, space, ent, done, scale);
                    counts.TryGetValue(kind, out var c);
                    counts[kind] = (c.Done + (done ? 1 : 0), c.Total + 1);
                }
                tr.Commit();
            }
            return (counts, kept);
        }

        private static void TurnOn(Transaction tr, Database db)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (string name in new[] { Registration.DoneLayer, Registration.PendingLayer })
            {
                var layer = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForRead);
                if (!layer.IsOff) continue;
                layer.UpgradeOpen();
                layer.IsOff = false;
            }
        }
    }
}
