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
        /// Copia os blocos do desenho aberto para dentro da biblioteca (Blocos\BLOCOS.dwg).
        /// Útil para aproveitar blocos criados num projeto sem precisar abrir o BLOCOS.dwg.
        /// </summary>
        [CommandMethod("FIBRA_EXPORTAR_BLOCOS")]
        public void ExportBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string? library = BlockRepository.LibraryFile;
            if (library != null && string.Equals(Path.GetFullPath(doc.Name), Path.GetFullPath(library), StringComparison.OrdinalIgnoreCase))
            {
                ed.WriteMessage($"\n[INFO]: Este desenho já é a biblioteca ({BlockRepository.LibraryFileName}). Basta salvá-lo.");
                return;
            }

            var pko = new PromptKeywordOptions("\nSe o bloco já existir na biblioteca [Manter/Sobrescrever] <Manter>: ", "Manter Sobrescrever")
            {
                AllowNone = true
            };
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status == PromptStatus.Cancel) return;
            bool overwrite = pkr.Status == PromptStatus.OK && pkr.StringResult == "Sobrescrever";

            var (added, replaced, kept, error) = BlockRepository.ExportToLibrary(db, overwrite);
            if (error != null)
            {
                ed.WriteMessage($"\n[ERRO]: {error}");
                return;
            }

            ed.WriteMessage($"\n[SUCESSO]: {BlockRepository.LibraryFile}: {added} bloco(s) novo(s)" +
                            (replaced > 0 ? $", {replaced} substituído(s)" : "") + ".");
            if (kept > 0) ed.WriteMessage($"\n[INFO]: {kept} bloco(s) já existiam na biblioteca e foram mantidos.");
            if (added + replaced > 0) ed.WriteMessage("\n[INFO]: A versão anterior da biblioteca foi guardada como BLOCOS.bak.");
        }
    }
}
