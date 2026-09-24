using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class OpticalBudgetCommand
    {
        /// <summary>
        /// Budget óptico de um enlace GPON (OLT → cliente): soma as perdas de fibra, emendas, conectores
        /// e splitters e compara com a faixa da classe do módulo óptico da OLT (B+ ou C+).
        /// Os valores de perda ficam em Dados\perdas_opticas.csv.
        /// </summary>
        [CommandMethod("FIBRA_BUDGET_OPTICO")]
        public void OpticalBudget()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            OpticalLossTable table = OpticalLossTable.Load(ed);

            // 1. Cabos do trecho
            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione os cabos do trecho (da OLT até a CTO/cliente): "
            };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK) return;

            double drawnLength = 0;
            int cableCount = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is Polyline poly && XDataTags.GetCableName(poly) != null)
                    {
                        drawnLength += poly.Length;
                        cableCount++;
                    }
                }
                tr.Commit();
            }

            if (cableCount == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum cabo de fibra do plugin na seleção.");
                return;
            }

            // 2. Parâmetros do enlace
            double? extra = AskDouble(ed, "\nComprimento adicional fora do desenho (drop, reservas técnicas) em metros", 0);
            if (extra == null) return;

            int? splices = AskInt(ed, "\nQuantidade de emendas por fusão (DIO + entre lances + CTO)", cableCount + 1);
            if (splices == null) return;

            int? connectors = AskInt(ed, "\nQuantidade de conectores (OLT, DIO, CTO, ONU)", 4);
            if (connectors == null) return;

            var pso2 = new PromptStringOptions("\nSplitters em cascata, separados por vírgula (ex: 1x8,1x8) <1x8,1x8>: ")
            {
                AllowSpaces = false
            };
            PromptResult splitRes = ed.GetString(pso2);
            if (splitRes.Status != PromptStatus.OK) return;
            string splitText = string.IsNullOrWhiteSpace(splitRes.StringResult) ? "1x8,1x8" : splitRes.StringResult;

            var splitters = new List<(string Ratio, double Loss)>();
            foreach (string ratio in splitText.Split(new[] { ',', ';', '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double? loss = table.Splitter(ratio);
                if (loss == null)
                {
                    ed.WriteMessage($"\n[ERRO]: Splitter '{ratio}' não cadastrado em {OpticalLossTable.FileName}.");
                    return;
                }
                splitters.Add((ratio.Trim().ToLowerInvariant(), loss.Value));
            }

            var pko = new PromptKeywordOptions("\nClasse do módulo óptico da OLT [Bmais/Cmais] <Cmais>: ", "Bmais Cmais")
            {
                AllowNone = true
            };
            PromptResult classRes = ed.GetKeywords(pko);
            if (classRes.Status == PromptStatus.Cancel) return;
            string laserClass = classRes.Status == PromptStatus.OK && classRes.StringResult == "Bmais" ? "B+" : "C+";

            // 3. Cálculo
            double lengthKm = (drawnLength + extra.Value) / 1000.0;
            double fiberLoss = lengthKm * table["atenuacao_db_km"];
            double spliceLoss = splices.Value * table["perda_emenda_db"];
            double connectorLoss = connectors.Value * table["perda_conector_db"];
            double splitterLoss = splitters.Sum(s => s.Loss);
            double total = fiberLoss + spliceLoss + connectorLoss + splitterLoss;

            double margin = table["margem_seguranca_db"];
            double min = table[$"classe_{laserClass}_min_db"];
            double max = table[$"classe_{laserClass}_max_db"];
            double available = max - total - margin;

            ed.WriteMessage("\n\n================================================");
            ed.WriteMessage("\n                BUDGET ÓPTICO                   ");
            ed.WriteMessage("\n================================================");
            ed.WriteMessage($"\nFibra: {lengthKm:F3} km x {table["atenuacao_db_km"]:F2} dB/km  = {fiberLoss,6:F2} dB");
            ed.WriteMessage($"\nEmendas: {splices} x {table["perda_emenda_db"]:F2} dB         = {spliceLoss,6:F2} dB");
            ed.WriteMessage($"\nConectores: {connectors} x {table["perda_conector_db"]:F2} dB      = {connectorLoss,6:F2} dB");
            foreach (var (ratio, loss) in splitters)
                ed.WriteMessage($"\nSplitter {ratio}                     = {loss,6:F2} dB");
            ed.WriteMessage("\n------------------------------------------------");
            ed.WriteMessage($"\nPERDA TOTAL DO ENLACE                = {total,6:F2} dB");
            ed.WriteMessage($"\nClasse {laserClass}: faixa {min:F0} a {max:F0} dB | margem de segurança {margin:F1} dB");

            if (available >= 0)
                ed.WriteMessage($"\n[OK]: Enlace atendido. Sobra de {available:F2} dB além da margem.");
            else
                ed.WriteMessage($"\n[REPROVADO]: Faltam {-available:F2} dB. Reduza splitters/emendas ou use classe superior.");

            if (total < min)
                ed.WriteMessage($"\n[ATENÇÃO]: Perda abaixo do mínimo da classe ({min:F0} dB): risco de saturar o receptor, avalie usar atenuador.");

            ed.WriteMessage("\n================================================\n");
        }

        private static double? AskDouble(Editor ed, string message, double defaultValue)
        {
            var opts = new PromptDoubleOptions($"{message} <{defaultValue}>: ")
            {
                AllowNone = true,
                AllowNegative = false
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            if (res.Status == PromptStatus.Cancel) return null;
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }

        private static int? AskInt(Editor ed, string message, int defaultValue)
        {
            var opts = new PromptIntegerOptions($"{message} <{defaultValue}>: ")
            {
                AllowNone = true,
                AllowNegative = false
            };
            PromptIntegerResult res = ed.GetInteger(opts);
            if (res.Status == PromptStatus.Cancel) return null;
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }
    }
}
