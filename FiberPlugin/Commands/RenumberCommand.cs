using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Renumera blocos clicando neles na ordem desejada: o primeiro de cada tipo pergunta o número,
    /// os seguintes recebem o próximo automaticamente (P-05, P-06, P-07...).
    /// Funciona em postes, CTO, CEO e em qualquer bloco com atributo NÚMERO/NUMERO/ID.
    /// </summary>
    public class RenumberCommand
    {
        /// <summary>O que o bloco selecionado é, para saber como gravar o número novo.</summary>
        private sealed class Target
        {
            public string Key = "";          // Sequência: POSTE, CTO, CEO ou o nome do bloco
            public string Label = "";        // P-05, CTO-03, "MEU BLOCO 12"
            public int Current;
            public PoleData? Pole;
            public BoxData? Box;
            public string? AttributeText;    // Blocos comuns: valor atual do atributo de número
        }

        [CommandMethod("FIBRA_RENUMERAR")]
        public void Renumber()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var next = new Dictionary<string, int>();   // Próximo número de cada sequência
            int? forced = null;                          // Número digitado pela opção Numero
            int changed = 0;

            ed.WriteMessage("\nClique nos blocos na ordem da nova numeração (Enter para terminar).");

            while (true)
            {
                string hint = forced != null ? $" (próximo: {forced})" : "";
                var peo = new PromptEntityOptions($"\nSelecione o bloco a renumerar{hint} [Numero]: ", "Numero")
                {
                    AllowNone = true
                };
                peo.SetRejectMessage("\nSelecione um bloco.");
                peo.AddAllowedClass(typeof(BlockReference), false);

                PromptEntityResult res = ed.GetEntity(peo);
                if (res.Status == PromptStatus.Cancel || res.Status == PromptStatus.None) break;

                if (res.Status == PromptStatus.Keyword)
                {
                    int? n = Poles.AskNumber(ed, forced ?? 1, "do próximo bloco selecionado");
                    if (n != null) forced = n;
                    continue;
                }
                if (res.Status != PromptStatus.OK) continue;

                Target? target;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    target = Identify(tr, (BlockReference)tr.GetObject(res.ObjectId, OpenMode.ForRead));
                    tr.Commit();
                }
                if (target == null)
                {
                    ed.WriteMessage("\n[AVISO]: Este bloco não tem numeração (não é poste, CTO/CEO nem tem atributo NÚMERO/ID).");
                    continue;
                }

                // Primeiro bloco de cada tipo: pergunta o número (sugere o atual)
                int number;
                if (forced != null) number = forced.Value;
                else if (next.TryGetValue(target.Key, out int n)) number = n;
                else
                {
                    int? asked = Poles.AskNumber(ed, target.Current > 0 ? target.Current : 1, "novo para " + target.Label);
                    if (asked == null) break;
                    number = asked.Value;
                }
                string newLabel;
                string? duplicate;
                try
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(res.ObjectId, OpenMode.ForWrite);
                        var space = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite);
                        newLabel = Apply(tr, db, space, br, target, number);
                        duplicate = FindDuplicate(tr, space, br.ObjectId, target, number);
                        tr.Commit();
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.OnLockedLayer)
                {
                    ed.WriteMessage($"\n[AVISO]: {target.Label} está em layer travada e não foi renumerado.");
                    continue;
                }

                // Só avança a sequência depois de gravar
                forced = null;
                next[target.Key] = number + 1;
                changed++;
                ed.WriteMessage($"\n[OK]: {target.Label} → {newLabel}");
                if (duplicate != null) ed.WriteMessage($"\n[AVISO]: {duplicate} também está em outro bloco. Renumere-o também.");
            }

            if (changed > 0)
            {
                ed.WriteMessage($"\n[INFO]: {changed} bloco(s) renumerado(s).");
                ed.Regen();
            }
        }

        private static Target? Identify(Transaction tr, BlockReference br)
        {
            PoleData? pole = XDataTags.ReadPole(br);
            if (pole != null)
            {
                return new Target { Key = "POSTE", Label = PoleData.NumberText(pole.Number), Current = pole.Number, Pole = pole };
            }

            BoxData? box = XDataTags.ReadBox(br);
            if (box != null)
            {
                return new Target { Key = box.Kind, Label = box.Id, Current = box.Number, Box = box };
            }

            string? text = CadHelpers.GetAttributeValue(tr, br, CadHelpers.NumberTags);
            if (text == null) return null;

            Match last = LastDigits(text);
            string name = CadHelpers.GetBlockName(tr, br);
            return new Target
            {
                Key = "BLOCO:" + name,
                Label = text.Trim().Length > 0 ? $"{name} {text}" : name,
                Current = last.Success && int.TryParse(last.Value, out int n) ? n : 0,
                AttributeText = text
            };
        }

        /// <summary>Grava o número novo no bloco (XData, atributo e texto ao lado). Retorna a identificação nova.</summary>
        private static string Apply(Transaction tr, Database db, BlockTableRecord space, BlockReference br, Target target, int number)
        {
            if (target.Pole != null)
            {
                WritePoleNumber(tr, db, space, br, target.Pole, number);
                return PoleData.NumberText(number);
            }

            if (target.Box != null)
            {
                WriteBoxNumber(tr, db, space, br, target.Box, number);
                return target.Box.Id;
            }

            // Bloco comum: troca só os dígitos, mantendo prefixo e zeros à esquerda ("N° 07" → "N° 08")
            string text = target.AttributeText ?? "";
            Match last = LastDigits(text);
            string value = last.Success
                ? text.Substring(0, last.Index) +
                  number.ToString("D" + last.Length, CultureInfo.InvariantCulture) +
                  text.Substring(last.Index + last.Length)
                : number.ToString(CultureInfo.InvariantCulture);

            SetNumberAttribute(tr, br, value);
            return value;
        }

        /// <summary>Número novo no poste (aberto para escrita): XData, atributo de número e texto ao lado.</summary>
        /// <param name="labels">Textos já levantados (PoleLabels.IndexLabels), para numerar muitos de uma vez.</param>
        internal static void WritePoleNumber(Transaction tr, Database db, BlockTableRecord space, BlockReference br, PoleData pole, int number,
            Dictionary<string, List<ObjectId>>? labels = null)
        {
            pole.Number = number;
            XDataTags.TagPole(tr, db, br, pole);
            SetNumberAttribute(tr, br, PoleData.NumberText(number));
            PoleLabels.Place(tr, db, space, br, pole, labels);
        }

        /// <summary>Número novo na CTO/CEO (aberta para escrita): XData, atributo de número e texto embaixo.</summary>
        internal static void WriteBoxNumber(Transaction tr, Database db, BlockTableRecord space, BlockReference br, BoxData box, int number,
            Dictionary<string, List<ObjectId>>? labels = null)
        {
            box.Number = number;
            XDataTags.TagBox(tr, db, br, box);
            SetNumberAttribute(tr, br, box.Id);
            PoleLabels.PlaceBox(tr, db, space, br, box, labels);
        }

        private static void SetNumberAttribute(Transaction tr, BlockReference br, string value) =>
            CadHelpers.SetAttributes(tr, br, tag => CadHelpers.IsTag(tag, CadHelpers.NumberTags) ? value : null);

        /// <summary>Outro bloco do mesmo tipo com o mesmo número (null se não houver).</summary>
        private static string? FindDuplicate(Transaction tr, BlockTableRecord space, ObjectId self, Target target, int number)
        {
            if (target.Pole != null)
            {
                bool dup = Poles.Collect(tr, space).Any(p => p.Id != self && p.Data != null && p.Data.Number == number);
                return dup ? PoleData.NumberText(number) : null;
            }
            if (target.Box != null)
            {
                bool dup = Boxes.Collect(tr, space).Any(b => b.Id != self && b.Data.Kind == target.Box.Kind && b.Data.Number == number);
                return dup ? target.Box.Id : null;
            }
            return null;
        }

        private static Match LastDigits(string text) => Regex.Match(text, @"\d+(?!.*\d)");
    }
}
