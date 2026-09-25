using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class InsertBlockCommand
    {
        // Este é o nome do comando que você vai digitar no AutoCAD
        [CommandMethod("FIBRA_INSERIR_BLOCO")]
        public void InsertBlock()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Blocos da biblioteca (BLOCOS.dwg).
            //    Postes têm comando próprio e a seta de esforço é colocada pelos cálculos.
            List<BlockEntry> blocks = BlockRepository.List()
                .Where(b => !Poles.IsPoleBlockName(b.Name))
                .Where(b => !b.Name.Equals(FiberSettings.EffortBlockName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (blocks.Count == 0)
            {
                ed.WriteMessage($"\n[ERRO]: Nenhum bloco na biblioteca ({BlockRepository.LibraryFile ?? "pasta Blocos não encontrada"}).");
                if (BlockRepository.LastError != null) ed.WriteMessage($"\n[ERRO]: {BlockRepository.LastError}");
                return;
            }

            string? blockName;

            // 2. Mostra a janela (UI) com a lista de blocos
            using (var form = new UI.BlockSelectionForm(blocks))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                blockName = form.SelectedBlock;
            }

            if (blockName == null || blockName.Length == 0) return;

            // 3. Importa da biblioteca se o bloco ainda não existir no desenho
            ObjectId blockId = BlockRepository.EnsureInDrawing(db, blockName);
            if (blockId.IsNull)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível carregar o bloco '{blockName}'.");
                return;
            }

            while (true)
            {
                // 4. Pede o ponto onde o bloco será inserido
                var ppo = new PromptPointOptions($"\nSelecione o ponto de inserção para '{blockName}' (ou aperte ENTER/ESC para sair): ")
                {
                    AllowNone = true
                };

                PromptPointResult pPtRes = ed.GetPoint(ppo);
                if (pPtRes.Status == PromptStatus.Cancel || pPtRes.Status == PromptStatus.None) break;
                if (pPtRes.Status != PromptStatus.OK) continue;

                Point3d insertionPoint = pPtRes.Value;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    // Automatiza os textos dos atributos conhecidos
                    CadHelpers.InsertBlock(tr, currentSpace, blockId, insertionPoint, 0, null, tag =>
                    {
                        if (tag.Equals("TIPO", StringComparison.OrdinalIgnoreCase)) return "1x16"; // Capacidade default genérica
                        return CadHelpers.CoordinateAttribute(tag, insertionPoint);
                    });

                    tr.Commit();
                }

                ed.UpdateScreen();
                ed.WriteMessage($"\n[AVISO]: Bloco '{blockName}' inserido com sucesso em X:{insertionPoint.X:F2}, Y:{insertionPoint.Y:F2}.");
            }

            ed.WriteMessage("\n[AVISO]: Inserção contínua finalizada. Se os blocos estiverem fora da vista, digite 'Z' e 'E' (Zoom Extents).");
        }
    }
}
