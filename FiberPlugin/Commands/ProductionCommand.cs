using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Produção do Dia: quantos metros de cabo, CTO, CEO, postes e reservas técnicas foram feitos no dia, somando os
    /// desenhos em que se trabalhou (veja ProductionTracker). Abre a janela já no dia de hoje.
    /// </summary>
    public class ProductionCommand
    {
        [CommandMethod("FIBRA_PRODUCAO")]
        public void ShowProduction()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            ProductionTracker.Start();
            List<LiveDrawing> open = ProductionTracker.OpenDrawings();
            ProductionLog log = ProductionLog.Default;

            DayProduction today = DayProduction.For(DateTime.Today, log.ReadDay(DateTime.Today), open, DateTime.Today);
            doc.Editor.WriteMessage($"\n[INFO]: Produção de hoje: {DayProduction.Describe(today.Total)}.");

            using (var form = new ProductionForm(log, open))
            {
                AcApp.ShowModalDialog(form);
            }
        }
    }
}
