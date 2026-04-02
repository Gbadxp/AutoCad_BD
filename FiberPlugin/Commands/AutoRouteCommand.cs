using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class AutoRouteCommand
    {
        [CommandMethod("FIBRA_ROTEAMENTO_AUTO")]
        public void AutoRouteCable()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Opções de cabos baseadas no novo repositório
            var cableOptions = FiberPlugin.Models.CableProvider.GetCables();

            FiberPlugin.Models.CableModel selectedCable = null;

            // Mostra a janela (UI) com a lista de cabos
            using (var form = new FiberPlugin.UI.CableSelectionForm(cableOptions))
            {
                var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                if (result != System.Windows.Forms.DialogResult.OK) return; // Cancela se o usuário fechar a janela
                
                selectedCable = form.SelectedCable;
            }

            if (selectedCable == null) return;

            // Solicitar seleção de blocos (postes, caixas, etc.)
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelecione os elementos (blocos) para o roteamento automático: ";
            
            // Filtrar apenas para blocos
            TypedValue[] tvs = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
            SelectionFilter filter = new SelectionFilter(tvs);

            PromptSelectionResult psr = ed.GetSelection(pso, filter);

            if (psr.Status != PromptStatus.OK || psr.Value.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: Selecione pelo menos 2 elementos para criar um roteamento.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Extrair pontos (posição) dos blocos selecionados
                List<Point3d> points = new List<Point3d>();

                foreach (SelectedObject so in psr.Value)
                {
                    BlockReference br = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (br != null)
                    {
                        points.Add(br.Position);
                    }
                }

                if (points.Count < 2)
                {
                    tr.Commit();
                    return;
                }

                // Garantir a layer baseada no cabo selecionado
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                
                string layerName = $"FIBRA_CABO_{selectedCable.ShortName.Replace(" ", "_")}";

                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord();
                    ltr.Name = layerName;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 3); // Verde
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                    lt.DowngradeOpen();
                }

                BlockTable bTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Algoritmo do Vizinho Mais Próximo (Nearest Neighbor) simplificado
                List<Point3d> orderedPoints = new List<Point3d>();
                List<Point3d> remainingPoints = new List<Point3d>(points);

                // Começa pelo primeiro ponto da seleção
                Point3d currentPoint = remainingPoints[0];
                orderedPoints.Add(currentPoint);
                remainingPoints.RemoveAt(0);

                while (remainingPoints.Count > 0)
                {
                    // Encontra o ponto não visitado mais próximo
                    Point3d nearestPoint = remainingPoints
                        .OrderBy(p => p.DistanceTo(currentPoint))
                        .First();

                    orderedPoints.Add(nearestPoint);
                    currentPoint = nearestPoint;
                    remainingPoints.Remove(nearestPoint);
                }

                // Desenhar a polilinha ligando os pontos
                using (Polyline originalPoly = new Polyline())
                {
                    for (int i = 0; i < orderedPoints.Count; i++)
                    {
                        originalPoly.AddVertexAt(i, new Point2d(orderedPoints[i].X, orderedPoints[i].Y), 0, 0, 0);
                    }

                    Polyline routePoly = null;
                    
                    // Desloca a linha 1.8m do centro para não sobrepor o poste
                    try
                    {
                        DBObjectCollection offsetCurves = originalPoly.GetOffsetCurves(1.8);
                        if (offsetCurves != null && offsetCurves.Count > 0)
                        {
                            routePoly = offsetCurves[0] as Polyline;
                        }
                    }
                    catch
                    {
                        // Caso a geometria exata impossibilite o offset
                    }

                    if (routePoly == null)
                    {
                        routePoly = (Polyline)originalPoly.Clone();
                    }

                    routePoly.Layer = layerName;

                    btr.AppendEntity(routePoly);
                    tr.AddNewlyCreatedDBObject(routePoly, true);

                    // TEXTOS VÃO A VÃO
                    for (int i = 0; i < routePoly.NumberOfVertices - 1; i++)
                    {
                        Point3d p1 = routePoly.GetPoint3dAt(i);
                        Point3d p2 = routePoly.GetPoint3dAt(i + 1);
                        double spanLen = p1.DistanceTo(p2);
                        
                        Point3d midPt = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);
                        double rawAng = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                        double textAng = rawAng;

                        // Ajusta o texto para ficar legível
                        if (textAng > Math.PI / 2.0 + 0.001 || textAng < -Math.PI / 2.0 - 0.001)
                        {
                            textAng += Math.PI;
                            if (textAng > Math.PI) textAng -= 2.0 * Math.PI;
                        }

                        // Offset visual
                        double offsetDist = db.Dimtxt * 0.2;
                        Point3d ptAbove = new Point3d(
                            midPt.X + Math.Cos(textAng + Math.PI / 2.0) * offsetDist,
                            midPt.Y + Math.Sin(textAng + Math.PI / 2.0) * offsetDist,
                            0);

                        Point3d ptBelow = new Point3d(
                            midPt.X + Math.Cos(textAng - Math.PI / 2.0) * offsetDist,
                            midPt.Y + Math.Sin(textAng - Math.PI / 2.0) * offsetDist,
                            0);

                        // Nome acima intercalado (um vão sim, um vão não)
                        if (i % 2 == 0)
                        {
                            using (MText txtName = new MText())
                            {
                                txtName.Attachment = AttachmentPoint.BottomCenter;
                                txtName.Location = ptAbove;
                                txtName.Contents = selectedCable.ShortName;
                                txtName.TextHeight = 2.0;
                                txtName.Rotation = textAng;
                                txtName.Layer = layerName;
                                
                                btr.AppendEntity(txtName);
                                tr.AddNewlyCreatedDBObject(txtName, true);
                            }
                        }

                        // Metragem abaixo
                        using (MText txtLen = new MText())
                        {
                            txtLen.Attachment = AttachmentPoint.TopCenter;
                            txtLen.Location = ptBelow;
                            txtLen.Contents = $"{spanLen:F1}m";
                            txtLen.TextHeight = 2.0;
                            txtLen.Rotation = textAng;
                            txtLen.Layer = layerName;

                            btr.AppendEntity(txtLen);
                            tr.AddNewlyCreatedDBObject(txtLen, true);
                        }
                    }

                    ed.WriteMessage($"\n[SUCESSO]: Roteamento automático do cabo '{selectedCable.FullName}' concluído! {orderedPoints.Count} elementos conectados. Metragem aproximada roteada: {routePoly.Length:F2} m");
                }

                tr.Commit();
            }
        }
    }
}
