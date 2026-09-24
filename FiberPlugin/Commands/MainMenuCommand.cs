using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class MainMenuCommand
    {
        [CommandMethod("FIBRA")]
        public void ShowMenu()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            
            string? cmdToExecute = null;

            using (var form = new FiberPlugin.UI.MainMenuForm())
            {
                var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    cmdToExecute = form.SelectedCommandName;
                }
            }

            if (!string.IsNullOrEmpty(cmdToExecute))
            {
                // Envia o comando escolhido de volta para a linha de comando do AutoCAD
                // O espaço ' ' funciona como o "Enter" no AutoCAD
                doc.SendStringToExecute($"{cmdToExecute} ", true, false, false);
            }
        }
    }
}
