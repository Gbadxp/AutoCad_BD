using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
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
            string? dir = ShowDataFolder();
            ed.WriteMessage(dir == null ? "\n[AVISO]: Pasta de dados do plugin não encontrada." : $"\n[INFO]: {dir}");
        }

        /// <summary>
        /// Abre no Explorer a pasta com as planilhas e o empresa.txt (Documentos\Fiber Plugin na instalação normal).
        /// Retorna a pasta, ou null se ela não existir. Usado também pela janela Dados do Projeto.
        /// </summary>
        internal static string? ShowDataFolder()
        {
            string? dir = PluginPaths.IsInstalled ? PluginPaths.UserRoot : Path.GetDirectoryName(PluginPaths.DataDir ?? "");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            return dir;
        }

        /// <summary>
        /// Atualiza a biblioteca de blocos sem reinstalar o plugin: passa a ler direto o BLOCOS.dwg escolhido
        /// (ex.: o da pasta do projeto), relê os blocos e troca no desenho aberto os blocos que mudaram.
        /// </summary>
        [CommandMethod("FIBRA_ATUALIZAR_BLOCOS")]
        public void UpdateBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var before = new HashSet<string>(BlockRepository.List().Select(b => b.Name), StringComparer.OrdinalIgnoreCase);

            string? custom = PluginPaths.CustomLibrary;
            if (custom != null && File.Exists(custom))
            {
                ed.WriteMessage($"\nBiblioteca de blocos: {custom}");
                string? answer = CadHelpers.AskKeyword(ed, "\nBlocos [Atualizar/Trocar/Padrao] <Atualizar>: ",
                    "Atualizar Trocar Padrao", "Atualizar");
                if (answer == null) return;
                if (answer == "Trocar" && !ChooseLibrary(custom)) return;
                if (answer == "Padrao")
                {
                    PluginPaths.CustomLibrary = null;
                    ed.WriteMessage($"\n[INFO]: Voltando a usar o {BlockRepository.LibraryFileName} que vem com o plugin.");
                }
            }
            else
            {
                if (custom != null) ed.WriteMessage($"\n[AVISO]: O arquivo escolhido antes não existe mais ({custom}). Escolha de novo.");
                if (!ChooseLibrary(BlockRepository.LibraryFile)) return;
            }

            List<BlockEntry> blocks = BlockRepository.List();
            if (BlockRepository.LastError != null) ed.WriteMessage($"\n[ERRO]: {BlockRepository.LastError}");

            int updated;
            try
            {
                updated = BlockRepository.UpdateDrawing(db);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível atualizar os blocos do desenho ({ex.Message}).");
                return;
            }
            ed.Regen();

            List<string> added = blocks.Select(b => b.Name).Where(n => !before.Contains(n)).ToList();
            ed.WriteMessage($"\n[SUCESSO]: {blocks.Count} bloco(s) na biblioteca ({BlockRepository.LibraryFile}).");
            if (added.Count > 0) ed.WriteMessage($"\n[INFO]: Novo(s): {string.Join(", ", added)}.");
            if (updated > 0) ed.WriteMessage($"\n[INFO]: {updated} bloco(s) do desenho atualizado(s) com a versão da biblioteca.");
        }

        /// <summary>Pergunta qual BLOCOS.dwg usar e grava a escolha. False se o usuário cancelar.</summary>
        private static bool ChooseLibrary(string? current)
        {
            using (var ofd = new System.Windows.Forms.OpenFileDialog())
            {
                ofd.Title = $"Escolha o seu {BlockRepository.LibraryFileName}";
                ofd.Filter = "Desenho do AutoCAD (*.dwg)|*.dwg";
                ofd.FileName = BlockRepository.LibraryFileName;
                if (current != null && Path.GetDirectoryName(current) is string dir && Directory.Exists(dir)) ofd.InitialDirectory = dir;
                if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;

                PluginPaths.CustomLibrary = ofd.FileName;
                return true;
            }
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
            ed.WriteMessage($"\nBlocos: {BlockRepository.LibraryFile ?? "(não encontrado)"}" +
                            (PluginPaths.CustomLibrary != null ? " (escolhido no Atualizar Blocos)" : " (do plugin)"));
            ed.WriteMessage($"\nBlocos na biblioteca: {BlockRepository.List().Count}\n");
        }
    }
}
