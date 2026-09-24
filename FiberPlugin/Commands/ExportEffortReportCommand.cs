using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class ExportEffortReportCommand
    {
        [CommandMethod("FIBRA_RELATORIO_ESFORCOS")]
        public void ExportEffortReport()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<CableModel> catalog = CableProvider.GetCables(ed);
            var rows = new List<(PoleInfo Pole, EffortResult Effort)>();
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1. Coleta todos os postes e todos os cabos
                List<PoleInfo> poles = Poles.Collect(tr, modelSpace);
                List<CableRun> runs = EffortCalculator.CollectCables(tr, modelSpace, catalog, unknown);

                // 2. Calcula o vetor de tração em cada poste
                foreach (PoleInfo pole in poles)
                {
                    rows.Add((pole, EffortCalculator.AtPole(runs, pole.Position, FiberSettings.PoleMatchTolerance)));
                }

                tr.Commit();
            }

            foreach (string name in unknown)
            {
                ed.WriteMessage($"\n[AVISO]: Cabo '{name}' não está na planilha de cabos e foi ignorado no cálculo.");
            }

            if (rows.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum bloco de poste foi encontrado no desenho atual.");
                return;
            }

            // 3. Exporta tudo para CSV
            using (var sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Filter = "Comma Separated Values (*.csv)|*.csv|All files (*.*)|*.*";
                sfd.Title = "Salvar Relatório de Esforços dos Postes";
                sfd.FileName = "Relatorio_Esforcos_Postes.csv";

                if (sfd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    ed.WriteMessage("\n[AVISO]: Exportação cancelada pelo usuário.");
                    return;
                }

                try
                {
                    using (var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        sw.WriteLine("Número do Poste;Nome do Poste;Qtd de Cabos;Cabos e Pesos;Esforço Resultante (kgf);" +
                                     "Ângulo Resultante (Graus);Esforço Nominal (kgf);Utilização (%);Situação");

                        // Ordena os postes por número (numericamente quando possível)
                        var sorted = rows
                            .OrderBy(r => Poles.ParseNumber(r.Pole.Number) ?? int.MaxValue)
                            .ThenBy(r => r.Pole.Number);

                        foreach (var (pole, effort) in sorted)
                        {
                            double? nominal = pole.NominalKgf;
                            string nominalStr = nominal != null ? $"{nominal:F0}" : "";
                            string usage = nominal > 0 ? $"{effort.Kgf / nominal.Value * 100:F0}" : "";
                            string cables = effort.Cables.Count > 0 ? string.Join(" + ", effort.Cables) : "0";

                            sw.WriteLine(string.Join(";",
                                Csv(pole.Number), Csv(pole.Name), effort.CableCount, Csv(cables),
                                $"{effort.Kgf:F2}", $"{effort.AngleDeg:F1}", nominalStr, usage,
                                Poles.Status(effort.Kgf, nominal)));
                        }
                    }

                    int exceeded = rows.Count(r => Poles.Status(r.Effort.Kgf, r.Pole.NominalKgf) == "EXCEDIDO");
                    int noNominal = rows.Count(r => r.Pole.NominalKgf == null);

                    ed.WriteMessage($"\n[SUCESSO]: Relatório de {rows.Count} postes exportado para: {sfd.FileName}");
                    if (exceeded > 0)
                        ed.WriteMessage($"\n[ATENÇÃO]: {exceeded} poste(s) com esforço ACIMA do nominal. Veja a coluna 'Situação'.");
                    if (noNominal > 0)
                        ed.WriteMessage($"\n[AVISO]: {noNominal} poste(s) sem esforço nominal no nome (use FIBRA_NOMEAR_POSTE, ex.: DT 11/200).");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[ERRO]: Não foi possível salvar o arquivo. Detalhes: {ex.Message}");
                }
            }
        }

        // Remove caracteres que poderiam quebrar o CSV
        private static string Csv(string value) => value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
    }
}
