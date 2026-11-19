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
        /// gravado na caixa e aparece no texto dela, embaixo do nome ("S1 1:8"); a caixa pode ter um splitter de cada nível.
        /// Remover tira os splitters da caixa clicada. Repete até o Enter.
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
                    ? "\nClique na CTO/CEO para tirar os splitters (Enter para terminar) [Marcar]: "
                    : "\nClique na CTO/CEO do splitter (Enter para terminar) [Remover]: ", remove ? "Marcar" : "Remover")
                {
                    AllowNone = true
                };
                peo.SetRejectMessage("\nClique numa CTO ou CEO.");
                peo.AddAllowedClass(typeof(BlockReference), false);
                PromptEntityResult res = ed.GetEntity(peo);
                if (res.Status == PromptStatus.Keyword)
                {
                    remove = res.StringResult == "Remover";
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
                    : $"\n[OK]: {box.Id}: {string.Join(", ", box.SplitterLines)}");
                ed.UpdateScreen();
            }

            if (changed > 0) ed.WriteMessage($"\n[INFO]: Splitters alterados em {changed} caixa(s). O Gerar Relatório conta os splitters por nível.");
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
