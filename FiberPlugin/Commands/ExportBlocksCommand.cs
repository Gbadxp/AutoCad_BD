using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class ExportBlocksCommand
    {
        /// <summary>
        /// Copia os blocos do desenho aberto (ex.: o TEMPLATE_BD_V1.dwg) para a pasta Blocos,
        /// um .dwg por bloco. Basta rodar uma vez com o template aberto para montar a biblioteca.
        /// </summary>
        [CommandMethod("FIBRA_EXPORTAR_BLOCOS")]
        public void ExportBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string? root = PluginPaths.BlocksDir;
            if (root == null)
            {
                root = Path.Combine(PluginPaths.AssemblyDir, PluginPaths.BlocksFolderName);
                ed.WriteMessage($"\n[AVISO]: Pasta Blocos não encontrada. Ela será criada em: {root}");
            }

            var pko = new PromptKeywordOptions("\nSe o bloco já existir na biblioteca [Manter/Sobrescrever] <Manter>: ", "Manter Sobrescrever")
            {
                AllowNone = true
            };
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status == PromptStatus.Cancel) return;
            bool overwrite = pkr.Status == PromptStatus.OK && pkr.StringResult == "Sobrescrever";

            var (exported, skipped, errors) = BlockRepository.ExportFromDrawing(db, root, overwrite);

            foreach (string error in errors) ed.WriteMessage($"\n[ERRO]: {error}");
            ed.WriteMessage($"\n[SUCESSO]: {exported} bloco(s) exportado(s) para {root}");
            if (skipped > 0) ed.WriteMessage($"\n[AVISO]: {skipped} bloco(s) já existiam e foram mantidos.");
            ed.WriteMessage("\n[DICA]: Confira as categorias (subpastas) e apague da pasta os blocos que não quiser usar.");
        }
    }
}
