using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>Comandos de manutenção do próprio plugin.</summary>
    public class PluginCommands
    {
        /// <summary>Abre no Explorer a pasta com a planilha de cabos e a biblioteca de blocos.</summary>
        [CommandMethod("FIBRA_ABRIR_PASTA")]
        public void OpenDataFolder()
        {
            Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;

            string? dir = PluginPaths.IsInstalled ? PluginPaths.UserRoot : Path.GetDirectoryName(PluginPaths.DataDir ?? "");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                ed.WriteMessage("\n[AVISO]: Pasta de dados do plugin não encontrada.");
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            ed.WriteMessage($"\n[INFO]: {dir}");
        }

        /// <summary>Recria a aba "Fibra" (caso ela suma após trocar de espaço de trabalho).</summary>
        [CommandMethod("FIBRA_RIBBON")]
        public void RecreateRibbon()
        {
            FiberRibbon.CreateTab();
        }

        [CommandMethod("FIBRA_SOBRE")]
        public void About()
        {
            Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage($"\n{PluginInfo.Name} v{PluginInfo.Version}");
            ed.WriteMessage($"\nEscala do desenho: 1:{DrawingScale.Get(AcApp.DocumentManager.MdiActiveDocument.Database)}");
            ed.WriteMessage($"\nDLL: {PluginPaths.AssemblyDir}");
            ed.WriteMessage($"\nDados: {PluginPaths.DataDir ?? "(não encontrada)"}");
            foreach (string dir in PluginPaths.BlockLibraryDirs) ed.WriteMessage($"\nBlocos: {dir}");
            ed.WriteMessage($"\nBlocos na biblioteca: {BlockRepository.List().Count}\n");
        }
    }
}
