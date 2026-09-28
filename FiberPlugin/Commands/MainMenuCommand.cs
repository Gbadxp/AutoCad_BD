using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class MainMenuCommand
    {
        /// <summary>Menu com todas as ferramentas; a escolhida é executada na linha de comando.</summary>
        [CommandMethod("FIBRA")]
        public void ShowMenu()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;

            using (var form = new UI.MainMenuForm())
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK || string.IsNullOrEmpty(form.SelectedCommandName)) return;

                // O espaço no fim funciona como o Enter
                doc.SendStringToExecute(form.SelectedCommandName + " ", true, false, false);
            }
        }
    }
}
