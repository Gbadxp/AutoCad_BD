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
    public class CalculateEffortCommand
    {
        [CommandMethod("FIBRA_CALCULAR_ESFORCO")]
        public void CalculateEffort()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            // 1. Seleção do Cabo
            CableModel? cable = CadHelpers.SelectCable(ed, "Calcular");
            if (cable == null) return;

            // 2. Pontos clicados (Sequência de Postes)
            List<Point3d>? points = CadHelpers.GetPointSequence(ed,
                $"\nCálculo: Cabo {cable.ShortName} ({cable.WeightKgKm} kg/km). Selecione o PRIMEIRO poste do cabo: ",
                "\nSelecione o PRÓXIMO poste (Enter para calcular e finalizar): ");
            if (points == null) return;

            if (points.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: São necessários pelo menos 2 postes para criar as tensões.");
                return;
            }

            // Seta de esforço: do desenho ou importada da pasta Blocos
            ObjectId arrowId = BlockRepository.EnsureInDrawing(db, FiberSettings.EffortBlockName);
            Traction traction = Traction.Load(db, ed);
            traction.WriteMethod(ed);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                List<PoleInfo> poles = Poles.Collect(tr, space);
                var markers = new EffortMarkers(tr, db, space, arrowId, poles);

                ed.WriteMessage("\n--- RESULTADOS DE ESFORÇOS ---");

                // 3. Cálculo de Esforço Vetorial (Tração Resultante); seta só onde a NDU 009 pede
                for (int i = 0; i < points.Count; i++)
                {
                    EffortResult result = EffortCalculator.AtPathIndex(points, i, span => traction.Tension(cable, span));

                    PoleInfo? pole = Poles.Nearest(poles, points[i], FiberSettings.PoleLinkRadius);
                    PoleLoad load = markers.Place(points[i], result, pole);
                    string label = pole != null ? "Poste " + pole.Number : $"P{i + 1}";
                    string arrow = result.NeedsArrow ? $"seta {load.ProjectKgf:F2} KGF, ANG. {result.AngleDeg:F0}°" : result.WithoutArrowReason!;
                    string? status = load.Text(pole);
                    ed.WriteMessage($"\n{label} | {result.Situation} | {result.Kgf:F2} kgf | {arrow}" + (status != null ? " | " + status : ""));
                }

                tr.Commit();
                ed.WriteMessage($"\n[INFO]: Esforço calculado em {points.Count} postes: {markers.Placed} com seta (fim de rede ou ângulo acima de " +
                                $"{EffortResult.ArrowDeflectionDeg:F0}°) e {markers.Skipped} em alinhamento, sem seta (NDU 009, Anexo B 2.2.18).");
                traction.WriteWarnings(ed);
            }
            ed.UpdateScreen();
        }
    }
}
