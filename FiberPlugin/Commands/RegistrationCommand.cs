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
        private const string MarkDone = "Cadastrar", MarkPending = "Pendente", ShowAll = "Todos", ShowPending = "Faltam",
                             ShowDone = "Feitos", Hide = "Ocultar";

        // Última opção e tipo usados (enquanto o AutoCAD estiver aberto)
        private static string _lastOption = MarkDone, _lastKind = "Todos";

        /// <summary>
        /// Cadastro na plataforma da Energisa:
        /// <list type="bullet">
        /// <item>Cadastrar / Pendente: seleciona uma área ou clica nos objetos (só de um tipo, se quiser: postes, CTO, CEO,
        /// cabos ou outros) e marca como cadastrados ou tira a marca;</item>
        /// <item>Todos / Faltam / Cadastrados: mostra as marcas (verde = cadastrado, vermelha = falta), só as que faltam ou
        /// só as cadastradas; Ocultar: esconde as marcas.</item>
        /// </list>
        /// As marcas ficam em layers que não imprimem e são refeitas a cada vez (acompanham o que mudou no desenho).
        /// </summary>
        [CommandMethod("FIBRA_CADASTRO", CommandFlags.UsePickSet)]
        public void Register()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            string? option = CadHelpers.AskKeyword(ed,
                $"\nCadastro na Energisa [{MarkDone}/{MarkPending}/{ShowAll}/{ShowPending}/{ShowDone}/{Hide}] <{_lastOption}>: ",
                string.Join(" ", MarkDone, MarkPending, ShowAll, ShowPending, ShowDone, Hide), _lastOption);
            if (option == null) return;
            _lastOption = option;

            if (option == Hide)
            {
                SetLayers(db, done: false, pending: false);
                ed.WriteMessage("\n[INFO]: Marcas do cadastro escondidas. Use Todos, Faltam ou Cadastrados para mostrar de novo.");
                ed.Regen();
                return;
            }

            int changed = 0, locked = 0;
            if (option == MarkDone || option == MarkPending)
            {
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
            }

            // Marcas refeitas com o desenho de agora, e as layers ligadas conforme a opção
            var (counts, keptMarks) = RedrawMarks(db);
            bool showDone = option != ShowPending, showPending = option != ShowDone;
            SetLayers(db, showDone, showPending);

            ed.WriteMessage("\n--- CADASTRO NA ENERGISA (cadastrados / total) ---");
            foreach (string kind in Registration.Kinds)
            {
                if (counts.TryGetValue(kind, out var c) && c.Total > 0) ed.WriteMessage($"\n{kind}: {c.Done} / {c.Total}");
            }
            int done2 = counts.Values.Sum(c => c.Done), total = counts.Values.Sum(c => c.Total);
            ed.WriteMessage(total == 0
                ? "\n[AVISO]: Nenhum poste, CTO, CEO, cabo ou bloco do plugin no desenho."
                : $"\n[INFO]: {done2} de {total} cadastrado(s), faltam {total - done2}. Verde = cadastrado, vermelho = falta " +
                  $"(layers {Registration.DoneLayer} e {Registration.PendingLayer}, não imprimem)" +
                  (showDone && showPending ? "." : showDone ? "; mostrando só os cadastrados." : "; mostrando só os que faltam."));
            if (keptMarks > 0)
                ed.WriteMessage($"\n[AVISO]: {keptMarks} marca(s) antiga(s) em layer travada ficaram: destrave as layers do cadastro.");
            ed.Regen();
        }

        /// <summary>
        /// Apaga as marcas antigas e desenha uma em cada objeto do projeto (verde ou vermelha). Retorna, por tipo, quantos
        /// estão cadastrados e o total, e quantas marcas antigas ficaram por estar em layer travada. Objeto oculto (janela
        /// Visibilidade) conta, mas fica sem marca.
        /// </summary>
        internal static (Dictionary<string, (int Done, int Total)> Counts, int KeptMarks) RedrawMarks(Database db)
        {
            var counts = new Dictionary<string, (int Done, int Total)>();
            int kept;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                CadHelpers.EnsureNonPlottingLayer(tr, db, Registration.DoneLayer, 3);
                CadHelpers.EnsureNonPlottingLayer(tr, db, Registration.PendingLayer, 1);

                var marks = new List<ObjectId>();
                var objects = new List<(Entity Ent, string Kind, bool Done)>();
                foreach (ObjectId id in space)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    if (Registration.IsMark(ent)) { marks.Add(id); continue; }
                    if (Registration.KindOf(tr, ent) is string kind) objects.Add((ent, kind, Registration.IsDone(ent)));
                }
                kept = CadHelpers.EraseUnlocked(tr, marks);

                double scale = DrawingScale.Factor(db);
                foreach (var (ent, kind, done) in objects)
                {
                    if (ent.Visible) Registration.DrawMark(tr, db, space, ent, done, scale);
                    counts.TryGetValue(kind, out var c);
                    counts[kind] = (c.Done + (done ? 1 : 0), c.Total + 1);
                }
                tr.Commit();
            }
            return (counts, kept);
        }

        /// <summary>Liga ou desliga as layers das marcas (verde e vermelha).</summary>
        private static void SetLayers(Database db, bool done, bool pending)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (var (name, on) in new[] { (Registration.DoneLayer, done), (Registration.PendingLayer, pending) })
                {
                    if (!layers.Has(name)) continue;
                    var layer = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForRead);
                    if (layer.IsOff == !on) continue;
                    layer.UpgradeOpen();
                    layer.IsOff = !on;
                }
                tr.Commit();
            }
        }
    }
}
