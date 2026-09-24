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
    public class InsertPoleCommand
    {
        // Comando exclusivo para inserir postes com numeração sequencial
        [CommandMethod("FIBRA_INSERIR_POSTE")]
        public void InsertPole()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Bloco do poste: do desenho ou da pasta Blocos. Se houver mais de um, o usuário escolhe.
            string? blockName = ChoosePoleBlock(db);
            if (blockName == null)
            {
                ed.WriteMessage("\n[ERRO]: Nenhum bloco com 'POSTE' no nome foi encontrado no desenho nem na pasta Blocos!");
                return;
            }

            ObjectId blockId = BlockRepository.EnsureInDrawing(db, blockName);
            if (blockId.IsNull)
            {
                ed.WriteMessage($"\n[ERRO]: Não foi possível carregar o bloco '{blockName}'.");
                return;
            }

            // 2. Numeração: continua a partir do maior número de poste já existente neste desenho
            int suggested;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                suggested = Poles.NextNumber(tr, space);
                tr.Commit();
            }

            var pio = new PromptIntegerOptions($"\nNúmero do primeiro poste <{suggested}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptIntegerResult pir = ed.GetInteger(pio);
            if (pir.Status == PromptStatus.Cancel) return;
            int poleCounter = pir.Status == PromptStatus.OK ? pir.Value : suggested;

            while (true)
            {
                var ppo = new PromptPointOptions($"\nSelecione o ponto de inserção para o Poste #{poleCounter} (ou aperte ENTER/ESC para sair): ")
                {
                    AllowNone = true
                };

                PromptPointResult pPtRes = ed.GetPoint(ppo);
                if (pPtRes.Status == PromptStatus.Cancel || pPtRes.Status == PromptStatus.None) break;
                if (pPtRes.Status != PromptStatus.OK) continue;

                Point3d insertionPoint = pPtRes.Value;
                int number = poleCounter;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    CadHelpers.InsertBlock(tr, currentSpace, blockId, insertionPoint, 0, null, tag =>
                    {
                        string t = tag.ToUpperInvariant();
                        if (CadHelpers.IsTag(tag, CadHelpers.NumberTags)) return $"N° {number}";
                        if (t == "COORDENADA_Y" || t == "COORDENADA Y") return $"{insertionPoint.Y.ToString("F2", CultureInfo.InvariantCulture)} m S";
                        if (t == "COORDENADA_X" || t == "COORDENADA X") return $"{insertionPoint.X.ToString("F2", CultureInfo.InvariantCulture)} m E";
                        return null;
                    });

                    tr.Commit();
                }

                poleCounter++;
                ed.UpdateScreen();
                ed.WriteMessage($"\n[AVISO]: Poste N° {number} inserido em X:{insertionPoint.X:F2}, Y:{insertionPoint.Y:F2}.");
            }

            ed.WriteMessage($"\n[AVISO]: Inserção de postes finalizada. Próximo poste será o N° {poleCounter}");
        }

        private static string? ChoosePoleBlock(Database db)
        {
            List<BlockEntry> candidates = BlockRepository.ListAll(db)
                .Where(b => Poles.IsPoleBlockName(b.Name))
                .ToList();

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0].Name;

            // Todos na mesma categoria para aparecerem juntos na janela
            foreach (BlockEntry c in candidates) c.Category = "Postes";

            using (var form = new UI.BlockSelectionForm(candidates, "Fiber Plugin - Escolher Bloco de Poste"))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return null;
                return form.SelectedBlock;
            }
        }
    }
}
