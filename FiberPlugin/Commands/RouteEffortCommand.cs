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
        /// Calcula o esforço em todos os postes do percurso dos cabos selecionados e coloca a seta onde a NDU 009 pede:
        /// fim de rede e deflexão acima de 10°, com resultante não nula (Anexo B 2.2.18 e item 16.3 h). Nos postes de
        /// passagem o esforço sai só na linha de comando, e a seta de um cálculo anterior é apagada.
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
            Traction traction = Traction.Load(db, ed);
            traction.WriteMethod(ed);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                // Cabos selecionados (com peso conhecido)
                var selectedRuns = new List<CableRun>();
                var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SelectedObject so in psr.Value)
                {
                    CableRun? run = EffortCalculator.ToCableRun(tr.GetObject(so.ObjectId, OpenMode.ForRead), catalog, traction, unknown);
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
                List<EffortStop> stops = EffortCalculator.Stops(selectedRuns, poles);

                List<CableRun> runsForEffort = onlySelected
                    ? selectedRuns
                    : EffortCalculator.CollectCables(tr, modelSpace, catalog, traction);

                var markers = new EffortMarkers(tr, db, modelSpace, arrowId);
                int exceeded = 0, replace = 0, withoutPole = 0;

                ed.WriteMessage($"\n--- ESFORÇOS NO PERCURSO ({(onlySelected ? "cabo selecionado" : "total no poste")}) ---");

                foreach (EffortStop stop in stops)
                {
                    PoleInfo? pole = stop.Pole;
                    EffortResult result = EffortCalculator.AtStop(runsForEffort, stop, poles);
                    if (result.CableCount == 0) continue;

                    // Vértice sem poste em linha reta é só um ponto do desenho do cabo: nem aparece (só perde a seta
                    // que versões antigas punham ali)
                    if (pole == null && !result.NeedsArrow)
                    {
                        if (!onlySelected) markers.Clear(stop.Point, null);
                        continue;
                    }
                    if (pole == null) withoutPole++;

                    // No modo Cabo a seta que já está no poste fica: ela pode ser do total (ex.: derivação) e o cabo
                    // selecionado sozinho não diz se o poste precisa dela
                    PoleLoad load = markers.Place(stop.Point, result, pole, clearWhenNotNeeded: !onlySelected);
                    if (load.Exceeded) exceeded++;
                    if (load.NeedsReplacement) replace++;

                    string label = pole != null ? $"Poste {pole.Number}" : $"Ponto sem poste ({stop.Point.X:F1}; {stop.Point.Y:F1})";
                    string arrow = result.NeedsArrow ? $"seta {load.ProjectKgf:F2} KGF, ANG. {result.AngleDeg:F0}°" : result.WithoutArrowReason!;
                    string? status = load.Text(pole);
                    ed.WriteMessage($"\n{label} | {result.Situation} | {result.Kgf:F2} kgf no cabo | {arrow}" +
                                    (status != null ? " | " + status : ""));
                }

                tr.Commit();

                ed.WriteMessage($"\n[SUCESSO]: {markers.Placed + markers.Skipped} ponto(s) calculado(s): {markers.Placed} com seta " +
                                $"(fim de rede ou ângulo acima de {EffortResult.ArrowDeflectionDeg:F0}°) e {markers.Skipped} sem seta, " +
                                "que a NDU 009 dispensa (Anexo B 2.2.18 e item 16.3 h).");
                if (markers.Cleared > 0)
                    ed.WriteMessage($"\n[INFO]: {markers.Cleared} seta(s) de cálculo anterior apagada(s) de pontos que não precisam dela.");
                if (withoutPole > 0)
                    ed.WriteMessage($"\n[AVISO]: {withoutPole} ponto(s) em que o cabo termina ou faz ângulo sem poste a até {FiberSettings.PoleLinkRadius:F0} m: " +
                                    "insira o poste para o cálculo levar a altura e o nominal.");
                if (exceeded > 0)
                    ed.WriteMessage($"\n[ATENÇÃO]: {exceeded} poste(s) com esforço ACIMA do nominal.");
                if (replace > 0)
                    ed.WriteMessage($"\n[ATENÇÃO]: {replace} poste(s) passam do limite do item 14.2 d da NDU 009 (50 daN até 300 daN, 100 daN a partir de 600 daN): a norma pede a substituição.");
                traction.WriteWarnings(ed);
            }
            ed.UpdateScreen();
        }
    }
}
