using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class ExportEffortReportCommand
    {
        class PoleData
        {
            public Point3d Location { get; set; }
            public string Number { get; set; }
            public string Name { get; set; }
            
            public int CablesCount { get; set; }
            public string CableWeightsStr { get; set; }
            public double ResultantEffort { get; set; }
            public double ResultantAngle { get; set; }
        }

        class CableData
        {
            public string Name { get; set; }
            public double WeightKgKm { get; set; }
            public Polyline Poly { get; set; }
        }

        [CommandMethod("FIBRA_RELATORIO_ESFORCOS")]
        public void ExportEffortReport()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<PoleData> poles = new List<PoleData>();
            List<CableData> cablesData = new List<CableData>();

            var catalogCables = FiberPlugin.Models.CableProvider.GetCables();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1. Coleta todos os blocos de Postes e todas as Polilinhas de Cabos
                foreach (ObjectId objId in modelSpace)
                {
                    DBObject obj = tr.GetObject(objId, OpenMode.ForRead);

                    if (obj is BlockReference br)
                    {
                        if (br.AttributeCollection.Count > 0)
                        {
                            string num = "";
                            string nome = "";
                            bool isPole = false;

                            foreach (ObjectId attId in br.AttributeCollection)
                            {
                                AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                                string tagStr = attRef.Tag.ToUpperInvariant().Trim();
                                
                                if (tagStr == "NÚMERO" || tagStr == "NUMERO" || tagStr == "ID")
                                {
                                    num = attRef.TextString;
                                    isPole = true; // Se tem essa Tag, assumimos que é um poste
                                }
                                else if (tagStr == "NOME" || tagStr == "TIPO" || tagStr == "INFO" || tagStr == "DESCRICAO")
                                {
                                    nome = attRef.TextString;
                                }
                            }

                            if (isPole)
                            {
                                poles.Add(new PoleData
                                {
                                    Location = br.Position,
                                    Number = string.IsNullOrEmpty(num) ? "-" : num,
                                    Name = string.IsNullOrEmpty(nome) ? "Poste" : nome
                                });
                            }
                        }
                    }
                    else if (obj is Polyline poly)
                    {
                        if (poly.Layer.StartsWith("FIBRA_CABO_", StringComparison.OrdinalIgnoreCase))
                        {
                            string shortName = poly.Layer.Substring("FIBRA_CABO_".Length).Replace("_", " ");
                            var cableModel = catalogCables.FirstOrDefault(c => c.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase));
                            
                            if (cableModel != null)
                            {
                                cablesData.Add(new CableData
                                {
                                    Name = cableModel.ShortName,
                                    WeightKgKm = cableModel.WeightKgKm,
                                    Poly = poly
                                });
                            }
                        }
                    }
                }

                // 2. Calcula as Interseções e Vetor de Tração para cada Poste
                foreach (var pole in poles)
                {
                    Vector2d totalResultant = new Vector2d(0, 0);
                    int cablesCountAtPole = 0;
                    List<string> weightsDesc = new List<string>();

                    foreach (var cable in cablesData)
                    {
                        double p = cable.WeightKgKm / 1000.0; // Kg/m
                        Polyline poly = cable.Poly;
                        bool touchesThisCable = false;

                        for (int v = 0; v < poly.NumberOfVertices; v++)
                        {
                            Point3d vertexPt = poly.GetPoint3dAt(v);
                            
                            if (vertexPt.DistanceTo(pole.Location) <= 2.5)
                            {
                                touchesThisCable = true;
                                cablesCountAtPole++;
                                
                                // O cabo faz tração para o vértice anterior
                                if (v > 0)
                                {
                                    Point3d prevPt = poly.GetPoint3dAt(v - 1);
                                    double L1 = vertexPt.DistanceTo(prevPt);
                                    double T1 = 12.5 * p * L1; 
                                    
                                    Vector2d vec1 = new Point2d(prevPt.X, prevPt.Y) - new Point2d(vertexPt.X, vertexPt.Y);
                                    totalResultant += vec1.GetNormal() * T1;
                                }

                                // O cabo faz tração para o vértice seguinte
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

                        if (touchesThisCable)
                        {
                            weightsDesc.Add($"{cable.Name} ({cable.WeightKgKm} kg/km)");
                        }
                    }

                    pole.CablesCount = cablesCountAtPole;
                    pole.CableWeightsStr = weightsDesc.Count > 0 ? string.Join(" + ", weightsDesc) : "0";
                    pole.ResultantEffort = totalResultant.Length;

                    double resAngle = pole.ResultantEffort > 0.1 ? totalResultant.Angle : 0;
                    double graus = resAngle * 180.0 / Math.PI;
                    if (graus < 0) graus += 360;
                    
                    pole.ResultantAngle = graus;
                }

                tr.Commit();
            }

            if (poles.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum bloco de poste foi encontrado no desenho atual.");
                return;
            }

            // 3. Exporta tudo para CSV
            using (System.Windows.Forms.SaveFileDialog sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Filter = "Comma Separated Values (*.csv)|*.csv|All files (*.*)|*.*";
                sfd.Title = "Salvar Relatório de Esforços dos Postes";
                sfd.FileName = "Relatorio_Esforcos_Postes.csv";

                if (sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                        {
                            sw.WriteLine("Número do Poste;Nome do Poste;Qtd de Cabos;Cabos e Pesos;Esforço Resultante (daN);Ângulo Resultante (Graus)");

                            // Ordena os postes por Número (tentando ordenar numericamente se for possível)
                            var sortedPoles = poles.OrderBy(p => 
                            {
                                int idx;
                                string cleanNum = new string(p.Number.Where(char.IsDigit).ToArray());
                                if (int.TryParse(cleanNum, out idx)) return idx;
                                return 999999;
                            }).ToList();

                            foreach (var pole in sortedPoles)
                            {
                                // Limpa caracteres que poderiam quebrar o CSV
                                string safeNumber = pole.Number.Replace(";", ",");
                                string safeName = pole.Name.Replace(";", ",");
                                
                                sw.WriteLine($"{safeNumber};{safeName};{pole.CablesCount};{pole.CableWeightsStr};{pole.ResultantEffort:F2};{pole.ResultantAngle:F1}");
                            }
                        }

                        ed.WriteMessage($"\n[SUCESSO]: Relatório de {poles.Count} postes exportado para: {sfd.FileName}");
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
