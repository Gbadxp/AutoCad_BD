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
    public class AutoRouteCommand
    {
        [CommandMethod("FIBRA_ROTEAMENTO_AUTO")]
        public void AutoRouteCable()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            CableModel? cable = CadHelpers.SelectCable(ed, "Lançar");
            if (cable == null) return;

            // Solicitar seleção de blocos (postes, caixas, etc.)
            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione os elementos (blocos) para o roteamento automático: "
            };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK || psr.Value.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: Selecione pelo menos 2 elementos para criar um roteamento.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var positions = new List<Point3d>();
                foreach (SelectedObject so in psr.Value)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br) positions.Add(br.Position);
                }

                // Blocos no mesmo ponto (ex.: CTO sobre o poste) viram um único ponto de passagem
                List<Point3d> points = RouteOptimizer.Dedupe(positions, 0.01);
                if (points.Count < 2)
                {
                    ed.WriteMessage("\n[AVISO]: Os elementos selecionados estão todos no mesmo ponto.");
                    tr.Commit();
                    return;
                }

                List<Point3d> ordered = RouteOptimizer.Order(points);

                // Afasta o cabo do centro do poste sem que os cantos se afastem mais que o previsto
                List<Point3d> vertices = RouteOptimizer.OffsetPath(ordered, FiberSettings.AutoRouteOffset);

                // A metragem escrita é o vão real entre postes
                var spans = Enumerable.Range(0, ordered.Count - 1).Select(i => ordered[i].DistanceTo(ordered[i + 1])).ToList();

                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                CableDrawing.Draw(tr, db, modelSpace, vertices, spans, cable);

                ed.WriteMessage($"\n[SUCESSO]: Roteamento automático do cabo '{cable.FullName}' concluído! " +
                                $"{ordered.Count} pontos conectados. Metragem entre postes: {spans.Sum():F2} m");

                tr.Commit();
            }
            ed.UpdateScreen();
        }
    }
}
