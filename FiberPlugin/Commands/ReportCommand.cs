using System.Diagnostics;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Relatório único do projeto em Excel (.xlsx), com uma aba para cada assunto:
    /// Resumo, Postes (com coordenadas e esforço), Esforços (cada ponto de esforço ligado ao poste de onde
    /// sai o cálculo) e Cabos (metragem por tipo).
    /// </summary>
    public class ReportCommand
    {
        [CommandMethod("FIBRA_RELATORIO")]
        public void GenerateReport()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            ProjectData project = ProjectData.Collect(db, CableProvider.GetCables());
            if (project.IsEmpty)
            {
                ed.WriteMessage("\n[AVISO]: Nada para relatar: o desenho não tem postes, pontos de esforço nem cabos do plugin.");
                return;
            }
            List<PoleInfo> poles = project.PoleList;
            List<BoxInfo> boxes = project.BoxList;
            List<EffortPoint> effortPoints = project.Efforts;
            List<CableTotal> cables = project.CableTotals;
            int legacyMarkers = project.LegacyMarkers;

            string title = Path.GetFileNameWithoutExtension(doc.Name);
            string? path = CadHelpers.AskSavePath("Salvar Relatório do Projeto", $"Relatorio_{title}.xlsx", "Planilha do Excel (*.xlsx)|*.xlsx");
            if (path == null)
            {
                ed.WriteMessage("\n[AVISO]: Relatório cancelado.");
                return;
            }

            var workbook = new XlsxWriter();
            UtmSettings? utm = UtmZone.Get(db);
            WriteSummary(workbook, doc, db, poles, boxes, effortPoints, cables, legacyMarkers);
            WritePoles(workbook, poles, effortPoints, utm);
            WriteBoxes(workbook, boxes, poles, utm);
            WriteEfforts(workbook, effortPoints);
            WriteCables(workbook, cables);
            WriteTableA(workbook, project.Occupied, boxes, poles, CompanyInfo.Load(out _), utm);
            WriteChecks(workbook, NormCheck.Run(project));

            try
            {
                workbook.Save(path);
            }
            catch (IOException ex)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível salvar ({ex.Message}). Se a planilha estiver aberta no Excel, feche-a e tente de novo.");
                return;
            }

            ed.WriteMessage($"\n[SUCESSO]: Relatório salvo em {path}");
            ed.WriteMessage($"\n[INFO]: {poles.Count} poste(s), {effortPoints.Count} ponto(s) de esforço, {cables.Count} tipo(s) de cabo.");
            if (legacyMarkers > 0)
            {
                ed.WriteMessage($"\n[AVISO]: {legacyMarkers} seta(s) de esforço de versões antigas, sem dados. Rode o Esforço no Percurso de novo para incluí-las.");
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (System.Exception)
            {
                // Sem Excel associado: o arquivo continua salvo
            }
        }

        private static void WriteSummary(XlsxWriter workbook, Document doc, Database db, List<PoleInfo> poles, List<BoxInfo> boxes,
            List<EffortPoint> effortPoints, IEnumerable<CableTotal> cables, int legacyMarkers)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("Resumo").ColumnWidths(42, 18);

            sheet.Title("Relatório do Projeto - " + Path.GetFileNameWithoutExtension(doc.Name)).Blank()
                 .Row("Arquivo", doc.Name)
                 .Row("Gerado em", DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
                 .Row("Escala do desenho", "1:" + DrawingScale.Get(db))
                 .Row("Gerado por", $"{PluginInfo.Name} v{PluginInfo.Version}")
                 .Blank();

            sheet.Header("Postes", "Quantidade");
            foreach (var group in poles.Where(p => p.Data != null).GroupBy(p => p.Data!.Designation).OrderBy(g => g.Key))
            {
                sheet.Row($"{group.Key} ({group.First().Data!.TypeName})", group.Count());
            }
            int withoutData = poles.Count(p => p.Data == null);
            if (withoutData > 0) sheet.Row("Sem tipo definido (postes antigos)", withoutData);
            sheet.Row("Total de postes", poles.Count).Blank();

            sheet.Header("CTO e CEO", "Quantidade");
            sheet.Row("CTO", boxes.Count(b => b.Data.Kind == BlockCategories.Cto));
            sheet.Row("CEO", boxes.Count(b => b.Data.Kind == BlockCategories.Ceo)).Blank();

            sheet.Header("Cabos", "Metragem (m)");
            foreach (CableTotal c in cables) sheet.Row(c.Description, Math.Round(c.Length, 2));
            sheet.Row("Total de cabos", Math.Round(cables.Sum(c => c.Length), 2)).Blank();

            sheet.Header("Esforços", "Quantidade");
            sheet.Row("Pontos de esforço calculados", effortPoints.Count);
            sheet.Row($"Com seta (fim de rede ou ângulo acima de {EffortResult.ArrowDeflectionDeg:F0}°)", effortPoints.Count(e => e.NeedsArrow));
            sheet.Row("Sem seta (passagem, a NDU 009 dispensa)", effortPoints.Count(e => !e.NeedsArrow));
            sheet.Row("Setas faltando no desenho", effortPoints.Count(e => e.NeedsArrow && !e.HasArrow));
            sheet.Row("Postes com esforço acima do nominal", effortPoints.Count(e => e.Load.Exceeded));
            sheet.Row("Postes acima do limite do item 14.2 d (substituir)", effortPoints.Count(e => e.Load.NeedsReplacement));
            sheet.Row("Pontos de esforço sem poste vinculado", effortPoints.Count(e => e.Pole == null));
            if (legacyMarkers > 0) sheet.Row("Setas antigas sem dados (recalcule)", legacyMarkers);
        }

        /// <summary>Seta do ponto no desenho: "Sim", "Falta", "Desatualizada" ou "Dispensada" (NDU 009, Anexo B 2.2.18).</summary>
        private static string ArrowText(EffortPoint e) =>
            !e.NeedsArrow ? "Dispensada" : !e.HasArrow ? "Falta" : e.ArrowOutdated ? "Desatualizada" : "Sim";

        private static void WritePoles(XlsxWriter workbook, List<PoleInfo> poles, List<EffortPoint> effortPoints, UtmSettings? utm)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("Postes")
                .ColumnWidths(10, 7, 14, 11, 16, 16, 11, 16, 16, 15, 16, 11, 22, 14, 18, 15, 13, 13, 13);

            sheet.Header("Número", "Tipo", "Descrição", "Altura (m)", "Nominal (daN)", "Nominal (kgf)", "Zona UTM", "Coordenada E (m)", "Coordenada N (m)",
                         "Poste", "Esforço no cabo (kgf)", "Ângulo (°)", "Situação", "Seta", "A 20 cm do topo (kgf)", "Existente (kgf)",
                         "Total (kgf)", "Utilização (%)", "Resultado");

            foreach (PoleInfo pole in poles)
            {
                PoleData? d = pole.Data;
                EffortPoint? effort = effortPoints.FirstOrDefault(e => e.Pole == pole);

                sheet.Row(
                    pole.Number,
                    d?.Type,
                    d?.TypeName,
                    d?.HeightM,
                    d?.EffortDaN,
                    Round(pole.NominalKgf, 2),
                    utm != null ? UtmZone.ZoneText(pole.Position, utm) : null,
                    Math.Round(pole.Position.X, 2),
                    Math.Round(pole.Position.Y, 2),
                    d?.Designation ?? pole.Name,
                    Round(effort?.Kgf, 2),
                    Round(effort?.AngleDeg, 1),
                    effort?.Situation,
                    effort != null ? ArrowText(effort) : null,
                    Round(effort?.Load.TopKgf, 2),
                    Round(effort?.Load.ExistingKgf, 2),
                    Round(effort?.Load.TotalKgf, 2),
                    Round(effort?.Load.Usage, 0),
                    effort?.Load.Result);
            }
        }

        private static void WriteBoxes(XlsxWriter workbook, List<BoxInfo> boxes, List<PoleInfo> poles, UtmSettings? utm)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("CTO e CEO").ColumnWidths(10, 7, 20, 14, 15, 11, 16, 16);
            sheet.Header("ID", "Tipo", "Bloco", "Poste", "Poste (tipo)", "Zona UTM", "Coordenada E (m)", "Coordenada N (m)");

            var polesByHandle = Poles.ByHandle(poles);
            foreach (BoxInfo box in boxes)
            {
                // Poste vinculado na inserção; se ele não existir mais ou estiver longe (cópia), o mais próximo agora
                PoleInfo? pole = Boxes.PoleOf(box, polesByHandle, poles);

                sheet.Row(
                    box.Data.Id,
                    box.Data.Kind,
                    box.BlockName,
                    pole?.Number ?? "Sem poste",
                    pole?.Data?.Designation ?? pole?.Name,
                    utm != null ? UtmZone.ZoneText(box.Position, utm) : null,
                    Math.Round(box.Position.X, 2),
                    Math.Round(box.Position.Y, 2));
            }
        }

        private static void WriteEfforts(XlsxWriter workbook, List<EffortPoint> effortPoints)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("Esforços")
                .ColumnWidths(8, 14, 15, 20, 14, 18, 11, 10, 18, 15, 13, 15, 14, 13, 14, 14, 60);

            sheet.Header("Ponto", "Poste de origem", "Poste", "Situação", "Seta", "Esforço no cabo (kgf)", "Ângulo (°)", "Cabos",
                         "A 20 cm do topo (kgf)", "Existente (kgf)", "Total (kgf)", "Nominal (kgf)", "Utilização (%)", "Resultado",
                         "Coordenada E", "Coordenada N", "Descrição");

            int n = 1;
            foreach (EffortPoint e in effortPoints)
            {
                string origin = e.Pole?.Number ?? "Sem poste";
                string from = e.Pole != null ? $"Cálculo a partir do poste {e.Pole.Number}" : $"Ponto sem poste a até {FiberSettings.PoleLinkRadius:F0} m";
                string description = $"{from} ({e.Situation}): esforço de {e.Kgf:F2} kgf com ângulo de {e.AngleDeg:F0}°." +
                                     (e.Load.NeedsReplacement ? $" Passa do limite de {e.Load.ReplacementLimitDaN:F0} daN do item 14.2 d: substituir o poste." : "");

                sheet.Row(
                    n++,
                    origin,
                    e.Pole?.Data?.Designation ?? e.Pole?.Name,
                    e.Situation,
                    ArrowText(e),
                    Math.Round(e.Kgf, 2),
                    Math.Round(e.AngleDeg, 1),
                    e.CableCount,
                    Round(e.Load.TopKgf, 2),
                    Round(e.Load.ExistingKgf, 2),
                    Round(e.Load.TotalKgf, 2),
                    Round(e.Load.NominalKgf, 2),
                    Round(e.Load.Usage, 0),
                    e.Load.Result,
                    Math.Round(e.Point.X, 2),
                    Math.Round(e.Point.Y, 2),
                    description);
            }
        }

        private static void WriteCables(XlsxWriter workbook, List<CableTotal> cables)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("Cabos").ColumnWidths(30, 16, 13, 9, 15);

            sheet.Header("Cabo", "Nome curto", "Peso (kg/km)", "Lances", "Metragem (m)");
            foreach (CableTotal c in cables)
            {
                sheet.Row(c.Description, c.Name, c.Model?.WeightKgKm, c.Runs, Math.Round(c.Length, 2));
            }
            sheet.Row("TOTAL", null, null, cables.Sum(c => c.Runs), Math.Round(cables.Sum(c => c.Length), 2));
        }

        /// <summary>
        /// Tabela A da NDU 009 (seção 23), obrigatória no projeto: uma linha por poste ocupado pelos cabos.
        /// O ID_Poste vem do FIBRA_ID_ENERGISA (em branco enquanto não informado).
        /// </summary>
        private static void WriteTableA(XlsxWriter workbook, List<(PoleInfo Pole, EffortResult Result)> occupied,
            List<BoxInfo> boxes, List<PoleInfo> poles, CompanyInfo? company, UtmSettings? utm)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("Tabela A (NDU 009)").ColumnWidths(12, 14, 44, 14, 46, 16, 24, 20);
            sheet.Title("Tabela A - Projeto de Uso Mútuo (NDU 009, seção 23)").Blank();
            sheet.Header("Poste (projeto)", "ID_Poste", "Coordenadas Georreferenciadas", "Tipo de Cabo", "Nome da Ocupante",
                         "Tipo de Companhia", "Tipo de Equipamento", "CNPJ da Companhia");

            var polesByHandle = Poles.ByHandle(poles);
            var withBox = new HashSet<PoleInfo>(boxes.Select(b => Boxes.PoleOf(b, polesByHandle, poles)).OfType<PoleInfo>());
            foreach (var (pole, result) in occupied)
            {
                string equipment = result.EndCount > 0 || result.MaxDeflectionDeg >= EffortResult.AngleThresholdDeg ? "Ancoragem" : "Suspensão";
                if (withBox.Contains(pole)) equipment += "; Equipamentos";

                string coordinates = (utm != null ? UtmZone.ZoneText(pole.Position, utm) + " " : "") +
                                     UtmZone.EastingText(pole.Position.X) + " " + UtmZone.NorthingText(pole.Position.Y, utm?.South ?? true);

                sheet.Row(
                    pole.Number,
                    pole.Data?.EnergisaId is { Length: > 0 } id ? id : null,
                    coordinates,
                    "Cabo Óptico",
                    company?.LegalName,
                    company?.CompanyType,
                    equipment,
                    company?.Cnpj);
            }
        }

        /// <summary>Não conformidades com a NDU 009 (as mesmas do Verificar Projeto).</summary>
        private static void WriteChecks(XlsxWriter workbook, List<NormIssue> issues)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("Verificação NDU 009").ColumnWidths(8, 26, 90, 14, 16, 16);
            sheet.Title("Verificação do projeto com a NDU 009").Blank();
            if (issues.Count == 0)
            {
                sheet.Row("Nenhuma não conformidade encontrada.");
                return;
            }

            sheet.Header("Tipo", "Onde", "Descrição", "Item da NDU", "Coordenada E", "Coordenada N");
            foreach (NormIssue i in issues)
            {
                bool located = i.Point != Autodesk.AutoCAD.Geometry.Point3d.Origin;
                sheet.Row(i.Severity, i.Where, i.Message, i.Rule,
                    located ? Math.Round(i.Point.X, 2) : (double?)null,
                    located ? Math.Round(i.Point.Y, 2) : (double?)null);
            }
        }

        private static double? Round(double? value, int digits) => value.HasValue ? Math.Round(value.Value, digits) : (double?)null;
    }
}
