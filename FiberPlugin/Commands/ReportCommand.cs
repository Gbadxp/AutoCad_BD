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
        private class EffortPoint
        {
            public EffortMarkerData Marker { get; set; } = new EffortMarkerData();
            public PoleInfo? Pole { get; set; }
            public double? NominalKgf => Pole?.NominalKgf;
            public string Result => Poles.Status(Marker.Kgf, NominalKgf);
            public double? Usage => NominalKgf > 0 ? Marker.Kgf / NominalKgf.Value * 100 : (double?)null;
        }

        [CommandMethod("FIBRA_RELATORIO")]
        public void GenerateReport()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<CableModel> catalog = CableProvider.GetCables();
            List<PoleInfo> poles;
            List<BoxInfo> boxes;
            var effortPoints = new List<EffortPoint>();
            List<CableTotal> cables;
            int legacyMarkers = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);

                poles = Poles.Collect(tr, modelSpace)
                    .OrderBy(p => Poles.ParseNumber(p.Number) ?? int.MaxValue)
                    .ThenBy(p => p.Number)
                    .ToList();
                var polesByHandle = poles.ToDictionary(p => p.Id.Handle.ToString(), StringComparer.OrdinalIgnoreCase);

                boxes = Boxes.Collect(tr, modelSpace)
                    .OrderBy(b => b.Data.Kind == BlockCategories.Cto ? 0 : 1)
                    .ThenBy(b => b.Data.Number)
                    .ToList();

                cables = CableDrawing.Totals(tr, modelSpace, catalog);

                var seenPoints = new HashSet<string>();
                var legacyPoints = new HashSet<string>();

                foreach (ObjectId id in modelSpace)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                    // Pontos de esforço (a seta e os textos de um mesmo ponto guardam os mesmos dados)
                    EffortMarkerData? marker = XDataTags.ReadEffortMarker(ent);
                    if (marker != null)
                    {
                        if (!seenPoints.Add(Key(marker.Point))) continue;

                        polesByHandle.TryGetValue(marker.PoleHandle, out PoleInfo? pole);
                        pole ??= Poles.Nearest(poles, marker.Point, 0.01);
                        effortPoints.Add(new EffortPoint { Marker = marker, Pole = pole });
                    }
                    else if (XDataTags.TryGetEffortPole(ent, out var oldPoint) && legacyPoints.Add(Key(oldPoint)))
                    {
                        legacyMarkers++;
                    }
                }

                tr.Commit();
            }

            if (poles.Count == 0 && boxes.Count == 0 && effortPoints.Count == 0 && cables.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nada para relatar: o desenho não tem postes, pontos de esforço nem cabos do plugin.");
                return;
            }

            // Pontos em ordem de poste; pontos sem poste no fim
            effortPoints = effortPoints
                .OrderBy(e => e.Pole == null)
                .ThenBy(e => e.Pole != null ? Poles.ParseNumber(e.Pole.Number) ?? int.MaxValue : int.MaxValue)
                .ToList();

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
            sheet.Row("Postes com esforço acima do nominal", effortPoints.Count(e => e.Result == "EXCEDIDO"));
            sheet.Row("Pontos de esforço sem poste vinculado", effortPoints.Count(e => e.Pole == null));
            if (legacyMarkers > 0) sheet.Row("Setas antigas sem dados (recalcule)", legacyMarkers);
        }

        private static void WritePoles(XlsxWriter workbook, List<PoleInfo> poles, List<EffortPoint> effortPoints, UtmSettings? utm)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("Postes")
                .ColumnWidths(10, 7, 14, 11, 16, 16, 11, 16, 16, 15, 16, 11, 22, 13, 13);

            sheet.Header("Número", "Tipo", "Descrição", "Altura (m)", "Nominal (daN)", "Nominal (kgf)", "Zona UTM", "Coordenada E (m)", "Coordenada N (m)",
                         "Poste", "Esforço (kgf)", "Ângulo (°)", "Situação", "Utilização (%)", "Resultado");

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
                    Round(effort?.Marker.Kgf, 2),
                    Round(effort?.Marker.AngleDeg, 1),
                    effort?.Marker.Situation,
                    Round(effort?.Usage, 0),
                    effort?.Result);
            }
        }

        private static void WriteBoxes(XlsxWriter workbook, List<BoxInfo> boxes, List<PoleInfo> poles, UtmSettings? utm)
        {
            XlsxWriter.Sheet sheet = workbook.AddSheet("CTO e CEO").ColumnWidths(10, 7, 20, 14, 15, 11, 16, 16);
            sheet.Header("ID", "Tipo", "Bloco", "Poste", "Poste (tipo)", "Zona UTM", "Coordenada E (m)", "Coordenada N (m)");

            foreach (BoxInfo box in boxes)
            {
                // Poste vinculado na inserção; se ele não existir mais, o mais próximo agora
                PoleInfo? pole = poles.FirstOrDefault(p => p.Id.Handle.ToString() == box.Data.PoleHandle)
                                 ?? Poles.Nearest(poles, box.Position, FiberSettings.PoleLinkRadius);

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
                .ColumnWidths(8, 14, 15, 20, 14, 11, 10, 15, 14, 13, 14, 14, 60);

            sheet.Header("Ponto", "Poste de origem", "Poste", "Situação", "Esforço (kgf)", "Ângulo (°)", "Cabos",
                         "Nominal (kgf)", "Utilização (%)", "Resultado", "Coordenada E", "Coordenada N", "Descrição");

            int n = 1;
            foreach (EffortPoint e in effortPoints)
            {
                EffortMarkerData m = e.Marker;
                string origin = e.Pole?.Number ?? "Sem poste";
                string from = e.Pole != null ? $"Cálculo a partir do poste {e.Pole.Number}" : $"Ponto sem poste a até {FiberSettings.PoleLinkRadius:F0} m";
                string description = $"{from} ({m.Situation}): esforço de {m.Kgf:F2} kgf com ângulo de {m.AngleDeg:F0}°.";

                sheet.Row(
                    n++,
                    origin,
                    e.Pole?.Data?.Designation ?? e.Pole?.Name,
                    m.Situation,
                    Math.Round(m.Kgf, 2),
                    Math.Round(m.AngleDeg, 1),
                    m.CableCount,
                    Round(e.NominalKgf, 2),
                    Round(e.Usage, 0),
                    e.Result,
                    Math.Round(m.Point.X, 2),
                    Math.Round(m.Point.Y, 2),
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

        private static double? Round(double? value, int digits) => value.HasValue ? Math.Round(value.Value, digits) : (double?)null;

        private static string Key(Autodesk.AutoCAD.Geometry.Point3d p) =>
            Math.Round(p.X, 2).ToString(CultureInfo.InvariantCulture) + ";" + Math.Round(p.Y, 2).ToString(CultureInfo.InvariantCulture);
    }
}
