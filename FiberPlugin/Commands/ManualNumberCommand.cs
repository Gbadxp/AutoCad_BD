using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class ManualNumberCommand
    {
        /// <summary>
        /// Numeração na ordem dos cliques: o usuário digita o número inicial (ex.: 10) e clica nos postes na ordem que
        /// quiser; cada clique já recebe o próximo número (P-10, P-11, P-12...), com o texto atualizado na hora. Desfazer
        /// volta o último clique e Numero muda o próximo número. No fim avisa os números que ficaram repetidos com postes
        /// que não foram clicados. Desfaz tudo com U, como qualquer comando.
        /// </summary>
        [CommandMethod("FIBRA_NUMERAR_MANUAL")]
        public void NumberByClicks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<PoleInfo> poles;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                poles = Poles.Collect(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead)).Where(p => p.Data != null).ToList();
                tr.Commit();
            }
            if (poles.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum poste inserido pelo plugin no desenho (Inserir Postes).");
                return;
            }

            int? start = Poles.AskNumber(ed, 1, "do primeiro poste clicado");
            if (start == null) return;
            int next = start.Value;

            // Texto de cada poste levantado só depois de confirmar o número (o levantamento apaga textos repetidos)
            Dictionary<string, List<ObjectId>> labels;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                labels = PoleLabels.IndexLabels(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                tr.Commit();
            }

            // Postes já numerados nesta sequência, com o número que tinham antes (para o Desfazer)
            var done = new List<(PoleInfo Pole, int Before, int Assigned)>();
            int locked = 0;

            while (true)
            {
                string hint = done.Count == 0 ? "" : " ou Enter para terminar";
                var ppo = new PromptPointOptions(
                    $"\nClique no poste que vai ser {PoleData.NumberText(next)}{hint} [Desfazer/Numero]: ", "Desfazer Numero")
                {
                    AllowNone = true
                };
                PromptPointResult res = ed.GetPoint(ppo);
                if (res.Status == PromptStatus.None || res.Status == PromptStatus.Cancel) break;

                if (res.Status == PromptStatus.Keyword)
                {
                    if (res.StringResult == "Numero")
                    {
                        if (Poles.AskNumber(ed, next, "do próximo poste clicado") is int n) next = n;
                    }
                    else if (done.Count == 0)
                    {
                        ed.WriteMessage("\n[INFO]: Nada para desfazer.");
                    }
                    else
                    {
                        var (pole, before, assigned) = done[done.Count - 1];
                        // Sem o índice: o texto pode ter sido criado agora, e o índice é de antes
                        string? undoError = AutoNumberCommand.Write(db, pole.Id,
                            (tr, br, space) => RenumberCommand.WritePoleNumber(tr, db, space, br, pole.Data!, before));
                        if (undoError == null)
                        {
                            done.RemoveAt(done.Count - 1);
                            next = assigned;
                            ed.WriteMessage($"\n[OK]: Desfeito: o poste voltou a ser {PoleData.NumberText(before)}.");
                            ed.UpdateScreen();
                        }
                        else
                        {
                            pole.Data!.Number = assigned; // A gravação foi desfeita: o número em memória continua o novo
                            ed.WriteMessage($"\n[AVISO]: Não foi possível desfazer ({undoError}).");
                        }
                    }
                    continue;
                }
                if (res.Status != PromptStatus.OK) continue;

                // Poste mais perto do clique, ou o do texto em que se clicou
                Point3d point = res.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                PoleInfo? target = Poles.Nearest(poles, point, FiberSettings.PoleLinkRadius) ?? AutoNumberCommand.PoleOfLabelAt(db, point, poles);
                if (target == null)
                {
                    ed.WriteMessage($"\n[AVISO]: Nenhum poste no clique: clique no poste (até {FiberSettings.PoleLinkRadius:F0} m dele) ou no texto dele.");
                    continue;
                }
                int clicked = done.FindIndex(d => d.Pole == target);
                if (clicked >= 0)
                {
                    ed.WriteMessage($"\n[AVISO]: Esse poste já é o {PoleData.NumberText(done[clicked].Assigned)} desta sequência (use Desfazer para voltar).");
                    continue;
                }

                // Índice dos textos só para quem já tinha texto ao começar (o texto criado agora não está nele)
                int old = target.Data!.Number;
                var index = labels.ContainsKey(target.Id.Handle.ToString()) ? labels : null;
                string? error = AutoNumberCommand.Write(db, target.Id,
                    (tr, br, space) => RenumberCommand.WritePoleNumber(tr, db, space, br, target.Data!, next, index));
                if (error != null)
                {
                    target.Data!.Number = old; // A gravação foi desfeita: o número em memória volta também
                    if (error == AutoNumberCommand.LockedLayer) locked++;
                    ed.WriteMessage($"\n[AVISO]: {PoleData.NumberText(old)} não foi renumerado ({error}).");
                    continue;
                }

                done.Add((target, old, next));
                ed.WriteMessage($"\n[OK]: {PoleData.NumberText(old)} → {PoleData.NumberText(next)}");
                ed.UpdateScreen(); // O número novo aparece já, antes do próximo clique
                next++;
            }

            if (done.Count == 0) return;
            ed.WriteMessage($"\n[SUCESSO]: {done.Count} poste(s) numerado(s) na ordem dos cliques, de " +
                            $"{PoleData.NumberText(done.Min(d => d.Assigned))} a {PoleData.NumberText(done.Max(d => d.Assigned))}.");
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} poste(s) em layer travada ficaram com o número antigo.");

            // Números desta sequência que agora aparecem em mais de um poste (um clicado e outro que ficou como estava);
            // repetições que já existiam antes, em números que não foram usados agora, não são deste comando
            var used = new HashSet<int>(done.Select(d => d.Assigned));
            List<string> repeated = poles.GroupBy(p => p.Data!.Number).Where(g => used.Contains(g.Key) && g.Count() > 1)
                .OrderBy(g => g.Key).Select(g => PoleData.NumberText(g.Key)).ToList();
            if (repeated.Count > 0)
            {
                ed.WriteMessage($"\n[AVISO]: Número(s) repetido(s) em mais de um poste: {string.Join(", ", repeated.Take(15))}" +
                                (repeated.Count > 15 ? $" e mais {repeated.Count - 15}" : "") +
                                ". Clique nos outros também, ou use a Numeração Automática.");
            }
            ed.Regen();
        }
    }
}
