using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class SplitterCommand
    {
        // Último nível e razão usados, sugeridos no próximo clique (enquanto o AutoCAD estiver aberto)
        private static int _lastLevel = 1, _lastRatio = 8;

        /// <summary>
        /// Marca os splitters das CTO/CEO: clica na caixa, digita o nível (1º, 2º ou 3º) e a razão (1:2 a 1:64). Ele fica
        /// gravado na caixa e aparece no texto dela, embaixo do nome; a caixa pode ter um splitter de cada nível.
        /// Remover tira os splitters da caixa clicada. Texto escolhe como o splitter aparece ("S1 1:8", "SPLITTER DE 1º
        /// NÍVEL", os dois juntos ou só "1:8"), para todos os desenhos, e já reescreve as caixas deste. Repete até o Enter.
        /// </summary>
        [CommandMethod("FIBRA_SPLITTER")]
        public void MarkSplitter()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            bool remove = false;
            int changed = 0;
            while (true)
            {
                var peo = new PromptEntityOptions(remove
                    ? "\nClique na CTO/CEO para tirar os splitters (Enter para terminar) [Marcar/Texto]: "
                    : "\nClique na CTO/CEO do splitter (Enter para terminar) [Remover/Texto]: ", (remove ? "Marcar" : "Remover") + " Texto")
                {
                    AllowNone = true
                };
                peo.SetRejectMessage("\nClique numa CTO ou CEO.");
                peo.AddAllowedClass(typeof(BlockReference), false);
                PromptEntityResult res = ed.GetEntity(peo);
                if (res.Status == PromptStatus.Keyword)
                {
                    if (res.StringResult == "Texto")
                    {
                        if (!ChooseLabel(ed, db)) break;
                    }
                    else remove = res.StringResult == "Remover";
                    continue;
                }
                if (res.Status == PromptStatus.Error) continue; // Clique no vazio
                if (res.Status != PromptStatus.OK) break;

                BoxData? box;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    box = XDataTags.ReadBox((Entity)tr.GetObject(res.ObjectId, OpenMode.ForRead));
                }
                if (box == null)
                {
                    ed.WriteMessage("\n[AVISO]: Isso não é uma CTO ou CEO do plugin (Inserir CTO / Inserir CEO).");
                    continue;
                }

                if (remove)
                {
                    if (box.Splitters.Count == 0)
                    {
                        ed.WriteMessage($"\n[INFO]: A {box.Id} não tem splitter marcado.");
                        continue;
                    }
                    box.Splitters.Clear();
                }
                else
                {
                    int? level = CadHelpers.AskInt(ed, $"\nNível do splitter na {box.Id} (1, 2 ou 3) <{_lastLevel}>: ", _lastLevel, 1, 3);
                    if (level == null) break;
                    int? ratio = AskRatio(ed, _lastRatio);
                    if (ratio == null) break;
                    _lastLevel = level.Value;
                    _lastRatio = ratio.Value;
                    // Um splitter por nível em cada caixa: marcar de novo o mesmo nível troca a razão
                    box.Splitters.RemoveAll(s => s.Level == level.Value);
                    box.Splitters.Add((level.Value, ratio.Value));
                    box.Splitters = box.Splitters.OrderBy(s => s.Level).ToList();
                }

                string? error = AutoNumberCommand.Write(db, res.ObjectId, (tr, br, space) =>
                {
                    XDataTags.TagBox(tr, db, br, box);
                    PoleLabels.PlaceBox(tr, db, space, br, box);
                });
                if (error != null)
                {
                    ed.WriteMessage($"\n[AVISO]: A {box.Id} ficou como estava ({error}).");
                    continue;
                }
                changed++;
                ed.WriteMessage(box.Splitters.Count == 0
                    ? $"\n[OK]: {box.Id} sem splitter."
                    : $"\n[OK]: {box.Id}: {string.Join(", ", box.SplitterDescriptions)}");
                ed.UpdateScreen();
            }

            if (changed > 0) ed.WriteMessage($"\n[INFO]: Splitters alterados em {changed} caixa(s). O Gerar Relatório conta os splitters por nível.");
        }

        /// <summary>
        /// Pergunta como o splitter aparece no texto das CTO/CEO, grava a escolha (vale para todos os desenhos) e reescreve
        /// as caixas com splitter deste desenho. False se o usuário cancelar.
        /// </summary>
        private static bool ChooseLabel(Editor ed, Database db)
        {
            UserSettings settings = UserSettings.Current;
            ed.WriteMessage("\nTexto do splitter na CTO/CEO (exemplo com 1º nível 1:8):" +
                            $"\n  Curto = {BoxData.SplitterText(1, 8, SplitterLabel.Short)}" +
                            $"   Aviso = {BoxData.SplitterText(1, 8, SplitterLabel.Notice)}" +
                            $"\n  Ambos = {BoxData.SplitterText(1, 8, SplitterLabel.NoticeAndRatio)}" +
                            $"   Razao = {BoxData.SplitterText(1, 8, SplitterLabel.RatioOnly)}");
            string current = UserSettings.SplitterLabelKeyword(settings.SplitterLabel);
            string? answer = CadHelpers.AskKeyword(ed, $"\nTexto do splitter [Curto/Aviso/Ambos/Razao] <{current}>: ",
                string.Join(" ", UserSettings.SplitterLabelKeywords.Select(k => k.Keyword)), current);
            if (answer == null) return false;
            SplitterLabel chosen = UserSettings.SplitterLabelKeywords.First(k => k.Keyword == answer).Format;
            if (chosen == settings.SplitterLabel)
            {
                ed.WriteMessage($"\n[INFO]: O texto do splitter já é esse ({BoxData.SplitterText(1, 8, chosen)}).");
                return true;
            }

            UserSettings changed = settings.Clone();
            changed.SplitterLabel = chosen;
            if (changed.Save() is string error)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível gravar a escolha ({error}).");
                return true;
            }

            // As caixas com splitter deste desenho passam para o texto novo (as de outros desenhos, quando forem mexidas)
            List<ObjectId> marked;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                marked = Boxes.Collect(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead))
                    .Where(b => b.Data.Splitters.Count > 0).Select(b => b.Id).ToList();
            }
            int updated = 0, failed = 0;
            foreach (ObjectId id in marked)
            {
                string? fail = AutoNumberCommand.Write(db, id, (tr, br, space) =>
                {
                    if (XDataTags.ReadBox(br) is BoxData data) PoleLabels.PlaceBox(tr, db, space, br, data);
                });
                if (fail == null) updated++;
                else failed++;
            }
            ed.WriteMessage($"\n[OK]: Texto do splitter: {BoxData.SplitterText(1, 8, chosen)} (vale também para os próximos desenhos)." +
                            (updated > 0 ? $" {updated} caixa(s) deste desenho atualizada(s)." : ""));
            if (failed > 0) ed.WriteMessage($"\n[AVISO]: {failed} caixa(s) ficaram com o texto antigo (layer travada?).");
            ed.UpdateScreen();
            return true;
        }

        /// <summary>Razão do splitter (1:X, potência de 2 de 2 a 64). Null se cancelar.</summary>
        private static int? AskRatio(Editor ed, int suggested)
        {
            while (true)
            {
                int? ratio = CadHelpers.AskInt(ed, $"\nRazão do splitter 1:X (2, 4, 8, 16, 32 ou 64) <{suggested}>: ", suggested, 2, 64);
                if (ratio == null) return null;
                if ((ratio.Value & (ratio.Value - 1)) == 0) return ratio;
                ed.WriteMessage("\n[AVISO]: Use 2, 4, 8, 16, 32 ou 64.");
            }
        }
    }
}
