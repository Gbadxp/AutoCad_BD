using System;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class NumberPointsCommand
    {
        [CommandMethod("FIBRA_NUMERAR_PONTOS")]
        public void NumberPoints()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string layerName = FiberSettings.CoordinatesLayer;
            int counter;

            // Garante que a layer existe e continua a numeração dos pontos já existentes (P01, P02...)
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadHelpers.EnsureLayer(tr, db, layerName, 4); // 4 = Cyan
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                counter = LastPointNumber(tr, space) + 1;
                tr.Commit();
            }

            ed.WriteMessage("\n[DICA]: Pressione Enter ou Esc a qualquer momento para sair do comando.");

            while (true)
            {
                var ppo = new PromptPointOptions($"\nSelecione a coordenada para o P{counter:D2}: ")
                {
                    AllowNone = true // Permite o Enter para sair
                };

                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status == PromptStatus.Cancel || ppr.Status == PromptStatus.None) break;
                if (ppr.Status != PromptStatus.OK) continue;

                Point3d insertPt = ppr.Value;

                // A coordenada X e Y base do clique
                double utmX = insertPt.X;
                double utmY = insertPt.Y;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    CadHelpers.AddText(tr, btr, insertPt, $"P{counter:D2}\\PX: {utmX:F2}\\PY: {utmY:F2}",
                        0, AttachmentPoint.TopLeft, layerName);
                    tr.Commit();
                }

                // Escreve na tela (console) para caso ele queira só ver sem se preocupar
                ed.WriteMessage($"\n[INSERIDO]: P{counter:D2} | X:{utmX:F2} | Y:{utmY:F2}");

                counter++; // Vai para o próximo ponto
            }
        }

        private static int LastPointNumber(Transaction tr, BlockTableRecord space)
        {
            int max = 0;
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not MText txt ||
                    !txt.Layer.Equals(FiberSettings.CoordinatesLayer, StringComparison.OrdinalIgnoreCase)) continue;

                Match m = Regex.Match(txt.Contents, @"^P(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
            }
            return max;
        }
    }
}
