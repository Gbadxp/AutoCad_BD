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

            string? mode = CadHelpers.AskKeyword(ed, "\nEsforço em cada poste [Total/Cabo] <Total>: ", "Total Cabo", "Total");
            if (mode == null) return;
            bool onlySelected = mode == "Cabo";

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

                CableProvider.ReportUnknown(ed, unknown);

                if (selectedRuns.Count == 0)
                {
                    ed.WriteMessage("\n[AVISO]: Nenhum cabo de fibra válido na seleção.");
                    tr.Commit();
                    return;
                }

                // Pontos do percurso: cada vértice é vinculado ao poste mais próximo (até PoleLinkRadius);
                // o esforço é calculado e desenhado a partir desse poste. Sem poste por perto, fica no vértice.
                List<PoleInfo> poles = Poles.Collect(tr, modelSpace);
                var stops = new List<(Point3d Point, PoleInfo? Pole, double Tolerance)>();
                foreach (CableRun run in selectedRuns)
                {
                    foreach (Point3d vertex in run.Vertices)
                    {
                        PoleInfo? pole = Poles.Nearest(poles, vertex, FiberSettings.PoleLinkRadius);
                        Point3d point = pole?.Position ?? vertex;

                        // O raio de busca dos cabos precisa alcançar o vértice que levou até este poste
                        double tolerance = Math.Max(FiberSettings.PoleMatchTolerance, vertex.DistanceTo(point) + 0.1);

                        int existing = stops.FindIndex(s => s.Point.DistanceTo(point) < 0.01);
                        if (existing >= 0)
                        {
                            if (tolerance > stops[existing].Tolerance) stops[existing] = (point, pole, tolerance);
                            continue;
                        }
                        stops.Add((point, pole, tolerance));
                    }
                }

                List<CableRun> runsForEffort = onlySelected
                    ? selectedRuns
                    : EffortCalculator.CollectCables(tr, modelSpace, catalog);

                var markers = new EffortMarkers(tr, db, modelSpace, arrowId);
                int exceeded = 0;

                ed.WriteMessage($"\n--- ESFORÇOS NO PERCURSO ({(onlySelected ? "cabo selecionado" : "total no poste")}) ---");

                foreach (var (point, pole, tolerance) in stops)
                {
                    EffortResult result = EffortCalculator.AtPole(runsForEffort, point, tolerance);
                    if (result.CableCount == 0) continue;

                    markers.Place(point, result, pole);

                    string label = pole != null ? $"Poste {pole.Number}" : $"Ponto sem poste ({point.X:F1}; {point.Y:F1})";
                    if (Poles.IsExceeded(pole, result.Kgf)) exceeded++;
                    string? status = Poles.StatusText(pole, result.Kgf);

                    ed.WriteMessage($"\n{label} | {result.Situation} | {result.Kgf:F2} kgf, ANG. {result.AngleDeg:F0}°" +
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
