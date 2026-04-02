using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class CalculateEffortCommand
    {
        [CommandMethod("FIBRA_CALCULAR_ESFORCO")]
        public void CalculateEffort()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Seleção do Cabo
            var cables = FiberPlugin.Models.CableProvider.GetCables();
            FiberPlugin.Models.CableModel selectedCable = null;

            using (var form = new FiberPlugin.UI.CableSelectionForm(cables))
            {
                var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                if (result != System.Windows.Forms.DialogResult.OK) return; 
                selectedCable = form.SelectedCable;
            }

            if (selectedCable == null) return;

            // Peso do cabo em KG/Metro
            double p = selectedCable.WeightKgKm / 1000.0; 

            // 2. Pontos clicados (Sequência de Postes)
            Point3dCollection points = new Point3dCollection();

            PromptPointOptions ppo = new PromptPointOptions($"\nCálculo: Cabo {selectedCable.ShortName} ({selectedCable.WeightKgKm} kg/km). Selecione o PRIMEIRO poste do cabo: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;
            points.Add(ppr.Value);

            while(true)
            {
                PromptPointOptions pNext = new PromptPointOptions("\nSelecione o PRÓXIMO poste (Enter para calcular e finalizar): ");
                pNext.UseBasePoint = true;
                pNext.BasePoint = points[points.Count - 1]; // Linha elástica
                pNext.AllowNone = true;
                
                PromptPointResult resNext = ed.GetPoint(pNext);
                
                if (resNext.Status == PromptStatus.Cancel) return;
                if (resNext.Status == PromptStatus.None) break;
                if (resNext.Status == PromptStatus.OK) points.Add(resNext.Value);
            }

            if (points.Count < 2) 
            {
                ed.WriteMessage("\n[AVISO]: São necessários pelo menos 2 postes para criar as tensões.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                
                // Garantir layer FIBRA_ESFORCOS
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                string layerName = "FIBRA_ESFORCOS";
                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord();
                    ltr.Name = layerName;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 1); // Vermelho
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                    lt.DowngradeOpen();
                }

                ed.WriteMessage($"\n--- RESULTADOS DE ESFORÇOS ---");

                // 3. Cálculo de Esforço Vetorial (Tração Resultante)
                for (int i = 0; i < points.Count; i++)
                {
                    Point3d pCurr = points[i];
                    Vector2d resultant = new Vector2d(0, 0);

                    if (i > 0)
                    {
                        Point3d pPrev = points[i - 1];
                        double L1 = pCurr.DistanceTo(pPrev);
                        // Fórmula: T = P * L^2 / (8 * f), sendo f = 0.01 * L -> T = 12.5 * P * L
                        double T1 = 12.5 * p * L1; 
                        
                        Vector2d v1 = new Point2d(pPrev.X, pPrev.Y) - new Point2d(pCurr.X, pCurr.Y);
                        resultant += v1.GetNormal() * T1;
                    }

                    if (i < points.Count - 1)
                    {
                        Point3d pNext = points[i + 1];
                        double L2 = pCurr.DistanceTo(pNext);
                        double T2 = 12.5 * p * L2;
                        
                        Vector2d v2 = new Point2d(pNext.X, pNext.Y) - new Point2d(pCurr.X, pCurr.Y);
                        resultant += v2.GetNormal() * T2;
                    }

                    double effortDaN = resultant.Length;

                    // Ajusta o ângulo do vetor para o texto apontar para fora do ângulo da curva (mostrando pra onde a força puxa o poste)
                    double resAngle = 0;
                    if (effortDaN > 0.1) 
                    {
                        resAngle = resultant.Angle;
                    } 
                    else if (i < points.Count - 1)
                    {
                        // Poste em suspensão perfeitamente reto (Esforço zero)
                        Vector2d dirLine = new Point2d(points[i + 1].X, points[i + 1].Y) - new Point2d(pCurr.X, pCurr.Y);
                        resAngle = dirLine.Angle + Math.PI / 2.0; 
                    }

                    // Posição visual do texto saltando do poste, seguindo o vetor
                    double offsetDist = 2.5; 
                    Point3d textLoc = new Point3d(
                        pCurr.X + Math.Cos(resAngle) * offsetDist,
                        pCurr.Y + Math.Sin(resAngle) * offsetDist,
                        0);

                    // Ajuste para não ficar de ponta-cabeça
                    double textRot = resAngle;
                    if (textRot > Math.PI / 2.0 + 0.001 || textRot < -Math.PI / 2.0 - 0.001)
                    {
                        textRot += Math.PI;
                        if (textRot > Math.PI) textRot -= 2.0 * Math.PI;
                    }

                    // Tenta inserir o Bloco "SETA DE ESFORÇO" se ele existir
                    string blockName = "SETA DE ESFORÇO";
                    bool blockInserted = false;
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                    if (bt.Has(blockName))
                    {
                        BlockTableRecord blockDef = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                        using (BlockReference blockRef = new BlockReference(pCurr, blockDef.ObjectId))
                        {
                            blockRef.Rotation = resAngle;
                            blockRef.Layer = layerName;

                            btr.AppendEntity(blockRef);
                            tr.AddNewlyCreatedDBObject(blockRef, true);

                            if (blockDef.HasAttributeDefinitions)
                            {
                                foreach (ObjectId id in blockDef)
                                {
                                    DBObject objAttr = tr.GetObject(id, OpenMode.ForRead);
                                    AttributeDefinition attDef = objAttr as AttributeDefinition;
                                    
                                    if (attDef != null && !attDef.Constant)
                                    {
                                        using (AttributeReference attRef = new AttributeReference())
                                        {
                                            attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                                            
                                            if (attDef.Tag.Equals("ESFORÇO_KFG", StringComparison.OrdinalIgnoreCase) || 
                                                attDef.Tag.Equals("ESFORCO_KFG", StringComparison.OrdinalIgnoreCase))
                                            {
                                                attRef.TextString = $"{effortDaN:F1} daN";
                                            }
                                            else if (attDef.Tag.Equals("336", StringComparison.OrdinalIgnoreCase) || 
                                                     attDef.Tag.Equals("ANGULO", StringComparison.OrdinalIgnoreCase))
                                            {
                                                double graus = resAngle * 180.0 / Math.PI;
                                                if (graus < 0) graus += 360;
                                                attRef.TextString = $"{graus:F1}\u00B0";
                                            }

                                            // Rotação do atributo preservada originariamente.
                                            // Correção de texto invertido removida para não desalinhar os atributos.

                                            blockRef.AttributeCollection.AppendAttribute(attRef);
                                            tr.AddNewlyCreatedDBObject(attRef, true);
                                        }
                                    }
                                }
                            }
                        }
                        blockInserted = true;
                    }

                    // Fallback: Se o bloco não existir
                    if (!blockInserted)
                    {
                        using (MText txt = new MText())
                        {
                            txt.Location = textLoc;
                            txt.Contents = $"{effortDaN:F1} daN"; // Tracionamento resultante final
                            txt.TextHeight = 2.0; 
                            txt.Rotation = textRot;
                            txt.Attachment = AttachmentPoint.BottomCenter;
                            txt.Layer = layerName;

                            btr.AppendEntity(txt);
                            tr.AddNewlyCreatedDBObject(txt, true);
                        }
                    }

                    string tipoPoste = i == 0 || i == points.Count - 1 ? "(Fim de Rota / Ancoragem)" : "(Passagem / Ângulo)";
                    ed.WriteMessage($"\nP{i+1}: {effortDaN:F1} daN {tipoPoste}");
                }

                tr.Commit();
                ed.WriteMessage($"\n[AVISO]: Esforço gerado em {points.Count} postes (calculado com flecha de 1%).");
                ed.UpdateScreen();
            }
        }
    }
}
