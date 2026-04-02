using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class CalculateTotalEffortCommand
    {
        [CommandMethod("FIBRA_ESFORCO_TOTAL")]
        public void CalculateTotalEffort()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var cables = FiberPlugin.Models.CableProvider.GetCables();

            while (true)
            {
                PromptPointOptions ppo = new PromptPointOptions("\nClique no poste (ponto exato do centro) para calcular a Rota Total (Enter para sair): ");
                ppo.AllowNone = true;
                PromptPointResult ppr = ed.GetPoint(ppo);
                
                if (ppr.Status == PromptStatus.Cancel || ppr.Status == PromptStatus.None) break;
                if (ppr.Status != PromptStatus.OK) continue;

                Point3d clickPoint = ppr.Value;
                Vector2d totalResultant = new Vector2d(0, 0);
                int cablesCount = 0;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btrModel = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Vasculha todos os objetos do desenho (Polilinhas dos Cabos)
                    foreach (ObjectId objId in btrModel)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        
                        if (obj is Polyline poly)
                        {
                            // Verifica se a layer indica que é um cabo nosso
                            if (poly.Layer.StartsWith("FIBRA_CABO_", StringComparison.OrdinalIgnoreCase))
                            {
                                // Extrai o nome do cabo ex: FIBRA_CABO_ASU-80_06F.O -> ASU-80 06F.O
                                string shortName = poly.Layer.Substring("FIBRA_CABO_".Length).Replace("_", " ");
                                
                                var cableModel = cables.FirstOrDefault(c => c.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase));
                                if (cableModel == null) continue; // Cabo desconhecido

                                double p = cableModel.WeightKgKm / 1000.0; // Peso Kg/m

                                // Verifica se o cabo passa pelo Poste que o usuário clicou (tolerância de 0.1)
                                for (int v = 0; v < poly.NumberOfVertices; v++)
                                {
                                    Point3d vertexPt = poly.GetPoint3dAt(v);
                                    
                                    if (vertexPt.DistanceTo(clickPoint) <= 2.5)
                                    {
                                        cablesCount++;
                                        
                                        // O cabo encosta aqui. Ele faz tração para o vértice anterior?
                                        if (v > 0)
                                        {
                                            Point3d prevPt = poly.GetPoint3dAt(v - 1);
                                            double L1 = vertexPt.DistanceTo(prevPt);
                                            double T1 = 12.5 * p * L1; // Tração = 1% flecha -> 12.5 * Peso * Vão
                                            
                                            Vector2d vec1 = new Point2d(prevPt.X, prevPt.Y) - new Point2d(vertexPt.X, vertexPt.Y);
                                            totalResultant += vec1.GetNormal() * T1;
                                        }

                                        // Ele faz tração para o vértice seguinte?
                                        if (v < poly.NumberOfVertices - 1)
                                        {
                                            Point3d nextPt = poly.GetPoint3dAt(v + 1);
                                            double L2 = vertexPt.DistanceTo(nextPt);
                                            double T2 = 12.5 * p * L2; 
                                            
                                            Vector2d vec2 = new Point2d(nextPt.X, nextPt.Y) - new Point2d(vertexPt.X, vertexPt.Y);
                                            totalResultant += vec2.GetNormal() * T2;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (cablesCount == 0)
                    {
                        ed.WriteMessage("\n[AVISO]: Nenhum cabo de fibra encontrado passando por este exato ponto.");
                        tr.Commit();
                        continue;
                    }

                    // Força Total
                    double effortDaN = totalResultant.Length;
                    double resAngle = effortDaN > 0.1 ? totalResultant.Angle : 0;

                    // Text/Block location offset if using MText
                    double offsetDist = 2.5; 
                    Point3d textLoc = new Point3d(
                        clickPoint.X + Math.Cos(resAngle) * offsetDist,
                        clickPoint.Y + Math.Sin(resAngle) * offsetDist,
                        0);

                    // Garantir layer FIBRA_ESFORCOS
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    string layerName = "FIBRA_ESFORCOS";
                    if (!lt.Has(layerName))
                    {
                        lt.UpgradeOpen();
                        LayerTableRecord ltr = new LayerTableRecord();
                        ltr.Name = layerName;
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 1);
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                        lt.DowngradeOpen();
                    }

                    // Tenta inserir o Bloco "SETA DE ESFORÇO" se ele existir
                    string blockName = "SETA DE ESFORÇO";
                    bool blockInserted = false;

                    if (bt.Has(blockName))
                    {
                        BlockTableRecord blockDef = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                        using (BlockReference blockRef = new BlockReference(clickPoint, blockDef.ObjectId))
                        {
                            blockRef.Rotation = resAngle;
                            blockRef.Layer = layerName;

                            btrModel.AppendEntity(blockRef);
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
                                            
                                            // Preenche os atributos solicitados
                                            if (attDef.Tag.Equals("ESFORÇO_KFG", StringComparison.OrdinalIgnoreCase) || 
                                                attDef.Tag.Equals("ESFORCO_KFG", StringComparison.OrdinalIgnoreCase))
                                            {
                                                attRef.TextString = $"{effortDaN:F1} daN";
                                            }
                                            else if (attDef.Tag.Equals("336", StringComparison.OrdinalIgnoreCase) || 
                                                     attDef.Tag.Equals("ANGULO", StringComparison.OrdinalIgnoreCase))
                                            {
                                                double graus = resAngle * 180.0 / Math.PI;
                                                // Normalizar para 0 a 360
                                                if (graus < 0) graus += 360;
                                                attRef.TextString = $"{graus:F1}\u00B0"; // Símbolo de grau
                                            }

                                            // Rotação do atributo preservada como original do bloco.
                                            // Remover inversão manual para evitar deslocamento de texto desalinhado.

                                            blockRef.AttributeCollection.AppendAttribute(attRef);
                                            tr.AddNewlyCreatedDBObject(attRef, true);
                                        }
                                    }
                                }
                            }
                        }
                        blockInserted = true;
                    }

                    // Fallback: Se o bloco não existir no desenho, usa o Texto normal
                    if (!blockInserted)
                    {
                        // Ajusta rotação para leitura
                        double textRot = resAngle;
                        if (textRot > Math.PI / 2.0 + 0.001 || textRot < -Math.PI / 2.0 - 0.001)
                        {
                            textRot += Math.PI;
                            if (textRot > Math.PI) textRot -= 2.0 * Math.PI;
                        }

                        using (MText txt = new MText())
                        {
                            txt.Location = textLoc;
                            txt.Contents = $"TOTAL: {effortDaN:F1} daN"; 
                            txt.TextHeight = 2.0; 
                            txt.Rotation = textRot;
                            txt.Attachment = AttachmentPoint.BottomCenter;
                            txt.Layer = layerName;

                            btrModel.AppendEntity(txt);
                            tr.AddNewlyCreatedDBObject(txt, true);
                        }
                    }

                    tr.Commit();
                    ed.UpdateScreen();
                    ed.WriteMessage($"\n[SUCESSO]: Poste calculado! {cablesCount} percursos de cabos encontrados. Esforço Total: {effortDaN:F1} daN.");
                }
            }
        }
    }
}
