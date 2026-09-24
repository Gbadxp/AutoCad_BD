using System;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(FiberPlugin.PluginApp))]

namespace FiberPlugin
{
    public static class PluginInfo
    {
        public const string Name = "Fiber Plugin";

        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
    }

    /// <summary>
    /// Ponto de entrada: roda quando o AutoCAD carrega a DLL (pelo pacote .bundle ou por NETLOAD).
    /// </summary>
    public class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            // Na abertura do AutoCAD a interface ainda não está pronta: termina no primeiro momento ocioso
            AcApp.Idle += OnFirstIdle;
        }

        public void Terminate()
        {
        }

        private static void OnFirstIdle(object? sender, EventArgs e)
        {
            AcApp.Idle -= OnFirstIdle;

            PluginPaths.EnsureUserFolders();
            FiberRibbon.Install();

            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage(
                $"\n{PluginInfo.Name} v{PluginInfo.Version} carregado. Digite FIBRA ou use a aba \"Fibra\" da faixa de opções.\n");
        }
    }
}
