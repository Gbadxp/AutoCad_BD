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
    public class CalculateTotalEffortCommand
    {
        [CommandMethod("FIBRA_ESFORCO_TOTAL")]
        public void CalculateTotalEffort()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<CableModel> catalog = CableProvider.GetCables(ed);
            ObjectId arrowId = BlockRepository.EnsureInDrawing(db, FiberSettings.EffortBlockName);

            while (true)
            {
                var ppo = new PromptPointOptions("\nClique no poste para calcular a Rota Total (Enter para sair): ")
                {
                    AllowNone = true
                };
                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status == PromptStatus.Cancel || ppr.Status == PromptStatus.None) break;
                if (ppr.Status != PromptStatus.OK) continue;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                    // Se clicou perto de um bloco de poste, usa o centro exato dele
                    PoleInfo? pole = Poles.Nearest(Poles.Collect(tr, modelSpace), ppr.Value, FiberSettings.PoleMatchTolerance);
                    Point3d polePoint = pole?.Position ?? ppr.Value;

                    var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    List<CableRun> runs = EffortCalculator.CollectCables(tr, modelSpace, catalog, unknown);
                    EffortResult result = EffortCalculator.AtPole(runs, polePoint, FiberSettings.PoleMatchTolerance);

                    foreach (string name in unknown)
                    {
                        ed.WriteMessage($"\n[AVISO]: Cabo '{name}' não está na planilha de cabos e foi ignorado.");
                    }

                    if (result.CableCount == 0)
                    {
                        ed.WriteMessage("\n[AVISO]: Nenhum cabo de fibra encontrado passando por este ponto.");
                        tr.Commit();
                        continue;
                    }

                    new EffortMarkers(tr, db, modelSpace, arrowId).Place(polePoint, result);
                    tr.Commit();

                    ed.WriteMessage($"\n[SUCESSO]: Poste calculado! {result.CableCount} cabo(s) encontrados. Esforço Total: {result.Kgf:F2} kgf.");

                    string? status = Poles.StatusText(pole, result.Kgf);
                    if (status != null) ed.WriteMessage($"\n           Poste {pole!.Number} | {status}");
                }
                ed.UpdateScreen();
            }
        }
    }
}
