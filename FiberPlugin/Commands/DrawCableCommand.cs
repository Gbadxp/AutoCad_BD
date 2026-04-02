using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class DrawCableCommand
    {
        [CommandMethod("FIBRA_LANCAR_CABO")]
        public void DrawCable()
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

            // Pontos clicados pelo usuário
            Point3dCollection points = new Point3dCollection();

            // Pede o primeiro ponto
            PromptPointOptions ppo = new PromptPointOptions("\nSelecione o ponto inicial do cabo: ");
            PromptPointResult ppr = ed.GetPoint(ppo);

            if (ppr.Status != PromptStatus.OK)
            {
                return;
            }

            points.Add(ppr.Value);

            // Continua pedindo os próximos pontos
            while (true)
            {
                PromptPointOptions ppoNext = new PromptPointOptions("\nSelecione o próximo ponto (ou pressione Enter para finalizar): ");
                ppoNext.UseBasePoint = true;
                ppoNext.BasePoint = points[points.Count - 1]; // Linha elástica a partir do último ponto
                ppoNext.AllowNone = true; // Permite Enter para sair

                PromptPointResult pprNext = ed.GetPoint(ppoNext);

                if (pprNext.Status == PromptStatus.Cancel)
                {
                    return; // Cancelar comando sem desenhar
                }
                else if (pprNext.Status == PromptStatus.None)
                {
                    break; // Enter pressionado, parar de pedir pontos
                }
                else if (pprNext.Status == PromptStatus.OK)
                {
                    points.Add(pprNext.Value);
                }
            }

            if (points.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: É necessário pelo menos dois pontos para lançar um cabo.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                // Ex: "FIBRA_CABO_ASU-80 06F.O"
                string layerName = $"FIBRA_CABO_{selectedCable.ShortName.Replace(" ", "_")}";

                // Garante que a layer do cabo específico existe
                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord();
                    ltr.Name = layerName;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 3); // 3 = Verde

                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                    lt.DowngradeOpen();
                }

                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Cria a Polilinha
                using (Polyline poly = new Polyline())
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        // Polyline 2D, ignorando o Z (podemos adicionar futuramente polilinhas 3D se for mandatório)
                        poly.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);
                    }

                    poly.Layer = layerName;

                    btr.AppendEntity(poly);
                    tr.AddNewlyCreatedDBObject(poly, true);

                    // TEXTOS VÃO A VÃO
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        Point3d p1 = points[i];
                        Point3d p2 = points[i + 1];
                        double spanLen = p1.DistanceTo(p2);
                        
                        Point3d midPt = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);
                        double rawAng = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                        double textAng = rawAng;

                        // Ajusta o texto para ficar legível (não ficar de cabeça para baixo)
                        if (textAng > Math.PI / 2.0 + 0.001 || textAng < -Math.PI / 2.0 - 0.001)
                        {
                            textAng += Math.PI;
                            if (textAng > Math.PI) textAng -= 2.0 * Math.PI;
                        }

                        // Offset visual para separar um pouco da linha
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

                    // Calcula o comprimento total
                    double totalLength = poly.Length;

                    ed.WriteMessage($"\n[AVISO]: Cabo '{selectedCable.FullName}' lançado com sucesso!");
                    ed.WriteMessage($"\n[COMPRIMENTO TOTAL]: {totalLength:F2} metros.");
                }

                tr.Commit();
                ed.UpdateScreen();
            }
        }
    }
}
