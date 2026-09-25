using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class RouteEffortCommand
    {
        /// <summary>
        /// Coloca a seta de esforço em todos os postes do percurso dos cabos selecionados.
        /// Modo Total: soma todos os cabos que passam em cada poste (igual ao FIBRA_ESFORCO_TOTAL).
        /// Modo Cabo: considera apenas os cabos selecionados.
        /// </summary>
        [CommandMethod("FIBRA_ESFORCO_PERCURSO")]
        public void RouteEffort()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var pko = new PromptKeywordOptions("\nEsforço em cada poste [Total/Cabo] <Total>: ", "Total Cabo")
            {
                AllowNone = true
            };
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status == PromptStatus.Cancel) return;
            bool onlySelected = pkr.Status == PromptStatus.OK && pkr.StringResult == "Cabo";

            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione o(s) cabo(s) do percurso: "
            };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK) return;

            List<CableModel> catalog = CableProvider.GetCables(ed);
            ObjectId arrowId = BlockRepository.EnsureInDrawing(db, FiberSettings.EffortBlockName);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                // Cabos selecionados (com peso conhecido)
                var selectedRuns = new List<CableRun>();
                var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SelectedObject so in psr.Value)
                {
                    CableRun? run = EffortCalculator.ToCableRun(tr.GetObject(so.ObjectId, OpenMode.ForRead), catalog, unknown);
                    if (run != null) selectedRuns.Add(run);
                }

                foreach (string name in unknown)
                {
                    ed.WriteMessage($"\n[AVISO]: Cabo '{name}' não está na planilha de cabos e foi ignorado.");
                }

                if (selectedRuns.Count == 0)
                {
                    ed.WriteMessage("\n[AVISO]: Nenhum cabo de fibra válido na seleção.");
                    tr.Commit();
                    return;
                }

                // Pontos do percurso: cada vértice vira o poste mais próximo (roteamento automático fica
                // 1,8 m afastado) ou o próprio vértice, se não houver bloco de poste ali.
                List<PoleInfo> poles = Poles.Collect(tr, modelSpace);
                var stops = new List<(Point3d Point, PoleInfo? Pole)>();
                foreach (CableRun run in selectedRuns)
                {
                    foreach (Point3d vertex in run.Vertices)
                    {
                        PoleInfo? pole = Poles.Nearest(poles, vertex, FiberSettings.PoleMatchTolerance);
                        Point3d point = pole?.Position ?? vertex;
                        if (stops.Any(s => s.Point.DistanceTo(point) < 0.01)) continue;
                        stops.Add((point, pole));
                    }
                }

                List<CableRun> runsForEffort = onlySelected
                    ? selectedRuns
                    : EffortCalculator.CollectCables(tr, modelSpace, catalog);

                var markers = new EffortMarkers(tr, db, modelSpace, arrowId);
                int exceeded = 0;

                ed.WriteMessage($"\n--- ESFORÇOS NO PERCURSO ({(onlySelected ? "cabo selecionado" : "total no poste")}) ---");

                foreach (var (point, pole) in stops)
                {
                    EffortResult result = EffortCalculator.AtPole(runsForEffort, point, FiberSettings.PoleMatchTolerance);
                    if (result.CableCount == 0) continue;

                    markers.Place(point, result);

                    string label = pole != null ? $"Poste {pole.Number}" : $"Ponto ({point.X:F1}; {point.Y:F1})";
                    if (Poles.IsExceeded(pole, result.Kgf)) exceeded++;
                    string? status = Poles.StatusText(pole, result.Kgf);

                    ed.WriteMessage($"\n{label}: {result.Kgf:F2} kgf ({result.CableCount} cabo(s))" +
                                    (status != null ? " | " + status : ""));
                }

                tr.Commit();

                ed.WriteMessage($"\n[SUCESSO]: Esforço colocado em {stops.Count} ponto(s) do percurso.");
                if (exceeded > 0)
                    ed.WriteMessage($"\n[ATENÇÃO]: {exceeded} poste(s) com esforço ACIMA do nominal.");
            }
            ed.UpdateScreen();
        }
    }
}
