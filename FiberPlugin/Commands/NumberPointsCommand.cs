using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class NumberPointsCommand
    {
        [CommandMethod("FIBRA_NUMERAR_PONTOS")]
        public void NumberPoints()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int counter = 1;
            string layerName = "FIBRA_COORDENADAS";

            // Garante que a layer existe
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord();
                    ltr.Name = layerName;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 4); // 4 = Cyan

                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                    lt.DowngradeOpen();
                }
                tr.Commit();
            }

            ed.WriteMessage("\n[DICA]: Pressione Enter ou Esc a qualquer momento para sair do comando.");

            while (true)
            {
                PromptPointOptions ppo = new PromptPointOptions($"\nSelecione a coordenada para o P{counter:D2}: ");
                ppo.AllowNone = true; // Permite o Enter para sair

                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status == PromptStatus.Cancel || ppr.Status == PromptStatus.None)
                {
                    break;
                }

                if (ppr.Status == PromptStatus.OK)
                {
                    Point3d insertPt = ppr.Value;

                    // A coordenada X e Y base do clique
                    double utmX = insertPt.X;
                    double utmY = insertPt.Y;

                    // Texto final a imprimir
                    string label = $"P{counter:D2}\\PX: {utmX:F2}\\PY: {utmY:F2}";

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        using (MText mText = new MText())
                        {
                            mText.Location = insertPt;
                            mText.Contents = label;
                            
                            // Ajusta o tamanho da fonte.
                            // Tamanho fixo de 2.0 unidades definido pelo usuário
                            mText.TextHeight = 2.0;
                            
                            mText.Layer = layerName;

                            btr.AppendEntity(mText);
                            tr.AddNewlyCreatedDBObject(mText, true);
                        }

                        tr.Commit();
                    }

                    // Escreve na tela (console) para caso ele queira só ver sem se preocupar
                    ed.WriteMessage($"\n[INSERIDO]: P{counter:D2} | X:{utmX:F2} | Y:{utmY:F2}");
                    
                    counter++; // Vai para o próximo ponto
                }
            }
        }
    }
}
