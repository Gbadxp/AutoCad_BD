using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class ExportBOMCommand
    {
        [CommandMethod("FIBRA_EXPORTAR_CSV")]
        public void ExportBOM()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            Dictionary<string, int> blockCounts = new Dictionary<string, int>();
            Dictionary<string, double> cableLengths = new Dictionary<string, double>();
            List<(string Name, double X, double Y)> pointsList = new List<(string, double, double)>();

            // Coleta os dados varrendo o ModelSpace
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId objId in modelSpace)
                {
                    DBObject obj = tr.GetObject(objId, OpenMode.ForRead);

                    // 1. Contagem de Blocos
                    if (obj is BlockReference br)
                    {
                        // Se for um bloco dinâmico, pega o nome real original
                        string blockName = br.IsDynamicBlock 
                            ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name 
                            : br.Name;

                        if (blockCounts.ContainsKey(blockName))
                        {
                            blockCounts[blockName]++;
                        }
                        else
                        {
                            blockCounts[blockName] = 1;
                        }
                    }
                    // 2. Soma da metragem de Cabos (agora separados por tipo)
                    else if (obj is Polyline poly)
                    {
                        if (poly.Layer.StartsWith("FIBRA_CABO_", StringComparison.OrdinalIgnoreCase))
                        {
                            // A layer é nomeada como FIBRA_CABO_ASU-80_06F.O
                            // Vamos extrair o nome original do cabo para o relatório
                            string layerName = poly.Layer;
                            string cableName = layerName.Substring(6).Replace("_", " "); // Remove "FIBRA_" e troca _ por espaço

                            if (cableLengths.ContainsKey(cableName))
                            {
                                cableLengths[cableName] += poly.Length;
                            }
                            else
                            {
                                cableLengths[cableName] = poly.Length;
                            }
                        }
                    }
                    // 3. Coordenadas de Pontos
                    else if (obj is MText mText && mText.Layer.Equals("FIBRA_COORDENADAS", StringComparison.OrdinalIgnoreCase))
                    {
                        string ptName = mText.Contents.Split(new string[] { "\\P" }, StringSplitOptions.None)[0];
                        pointsList.Add((ptName, mText.Location.X, mText.Location.Y));
                    }
                    else if (obj is DBText dbText && dbText.Layer.Equals("FIBRA_COORDENADAS", StringComparison.OrdinalIgnoreCase))
                    {
                        pointsList.Add((dbText.TextString, dbText.Position.X, dbText.Position.Y));
                    }
                }
                tr.Commit();
            }

            // Exibe janela para salvar o arquivo (Usando o OpenFileDialog nativo do Windows Forms pois definimos <UseWindowsForms>true</UseWindowsForms>)
            using (System.Windows.Forms.SaveFileDialog sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Filter = "Comma Separated Values (*.csv)|*.csv|All files (*.*)|*.*";
                sfd.Title = "Salvar Lista de Materiais";
                sfd.FileName = "BOM_Projeto_Fibra.csv";

                if (sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                        {
                            // Cabeçalho da Lista de Materiais
                            sw.WriteLine("--- LISTA DE MATERIAIS ---");
                            sw.WriteLine("Item;Quantidade;Unidade");
                            
                            // Lista de Blocos
                            foreach (var kvp in blockCounts.OrderBy(x => x.Key))
                            {
                                sw.WriteLine($"{kvp.Key};{kvp.Value};UN");
                            }

                            // Lista de Cabos
                            foreach (var kvp in cableLengths.OrderBy(x => x.Key))
                            {
                                if (kvp.Value > 0)
                                {
                                    sw.WriteLine($"{kvp.Key};{kvp.Value:F2};Metros");
                                }
                            }

                            // Lista de Coordenadas
                            if (pointsList.Count > 0)
                            {
                                sw.WriteLine();
                                sw.WriteLine("--- LISTA DE COORDENADAS ---");
                                sw.WriteLine("Ponto;X;Y");
                                
                                foreach (var pt in pointsList.OrderBy(p => p.Name))
                                {
                                    sw.WriteLine($"{pt.Name};{pt.X:F2};{pt.Y:F2}");
                                }
                            }
                        }

                        ed.WriteMessage($"\n[SUCESSO]: Lista de Materiais (BOM) exportada para: {sfd.FileName}");
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[ERRO]: Não foi possível salvar o arquivo. Detalhes: {ex.Message}");
                    }
                }
                else
                {
                    ed.WriteMessage("\n[AVISO]: Exportação cancelada pelo usuário.");
                }
            }
        }
    }
}
