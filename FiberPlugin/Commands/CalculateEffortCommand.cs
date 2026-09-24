using System;
using System.Collections.Generic;
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

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var markers = new EffortMarkers(tr, db, space, arrowId);
                List<PoleInfo> poles = Poles.Collect(tr, space);

                ed.WriteMessage("\n--- RESULTADOS DE ESFORÇOS ---");

                // 3. Cálculo de Esforço Vetorial (Tração Resultante)
                for (int i = 0; i < points.Count; i++)
                {
                    EffortResult result = EffortCalculator.AtPathIndex(points, i, cable.WeightKgKm);

                    // Poste em suspensão perfeitamente reto (esforço zero): seta perpendicular à linha
                    Point3d next = i < points.Count - 1 ? points[i + 1] : points[i];
                    Point3d prev = i < points.Count - 1 ? points[i] : points[i - 1];
                    double perpendicular = Math.Atan2(next.Y - prev.Y, next.X - prev.X) + Math.PI / 2.0;

                    markers.Place(points[i], result, angleOverride: perpendicular);

                    string tipoPoste = i == 0 || i == points.Count - 1 ? "(Fim de Rota / Ancoragem)" : "(Passagem / Ângulo)";
                    string situacao = "";
                    PoleInfo? pole = Poles.Nearest(poles, points[i], FiberSettings.PoleMatchTolerance);
                    if (pole?.NominalKgf != null)
                    {
                        situacao = $" | {pole.Name}: {Poles.Status(result.Kgf, pole.NominalKgf)}";
                    }

                    ed.WriteMessage($"\nP{i + 1}: {result.Kgf:F2} kgf {tipoPoste}{situacao}");
                }

                tr.Commit();
                ed.WriteMessage($"\n[AVISO]: Esforço gerado em {points.Count} postes (calculado com flecha de 1%).");
            }
            ed.UpdateScreen();
        }
    }
}
