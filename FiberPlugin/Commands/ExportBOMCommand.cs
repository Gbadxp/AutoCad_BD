using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class ExportBOMCommand
    {
        // Lista de blocos que não entram na lista de materiais (carimbo, norte, legenda...)
        private const string IgnoreFileName = "bom_ignorar.txt";

        [CommandMethod("FIBRA_EXPORTAR_CSV")]
        public void ExportBOM()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<CableModel> catalog = CableProvider.GetCables(ed);
            HashSet<string> ignoredBlocks = LoadIgnoredBlocks();

            var blockCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var cableLengths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var pointsList = new List<(string Name, double X, double Y)>();

            // Coleta os dados varrendo o ModelSpace
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId objId in modelSpace)
                {
                    DBObject obj = tr.GetObject(objId, OpenMode.ForRead);

                    // 1. Contagem de Blocos (sem as setas de esforço e os blocos da lista de ignorados)
                    if (obj is BlockReference br)
                    {
                        if (br.Layer.Equals(FiberSettings.EffortLayer, StringComparison.OrdinalIgnoreCase)) continue;

                        string blockName = CadHelpers.GetBlockName(tr, br);
                        if (blockName.Equals(FiberSettings.EffortBlockName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (ignoredBlocks.Contains(blockName)) continue;

                        blockCounts[blockName] = blockCounts.TryGetValue(blockName, out int count) ? count + 1 : 1;
                    }
                    // 2. Soma da metragem de Cabos, separados por tipo
                    else if (obj is Polyline poly)
                    {
                        string? cableName = XDataTags.GetCableName(poly);
                        if (cableName == null) continue;

                        // Descrição completa do catálogo quando o cabo está cadastrado
                        CableModel? model = CableProvider.Find(catalog, cableName);
                        string item = model != null ? $"Cabo {model.FullName} ({model.ShortName})" : $"Cabo {cableName}";

                        cableLengths[item] = cableLengths.TryGetValue(item, out double len) ? len + poly.Length : poly.Length;
                    }
                    // 3. Coordenadas de Pontos
                    else if (obj is MText mText && mText.Layer.Equals(FiberSettings.CoordinatesLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        string ptName = mText.Contents.Split(new[] { "\\P" }, StringSplitOptions.None)[0];
                        pointsList.Add((ptName, mText.Location.X, mText.Location.Y));
                    }
                    else if (obj is DBText dbText && dbText.Layer.Equals(FiberSettings.CoordinatesLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        pointsList.Add((dbText.TextString, dbText.Position.X, dbText.Position.Y));
                    }
                }
                tr.Commit();
            }

            using (var sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Filter = "Comma Separated Values (*.csv)|*.csv|All files (*.*)|*.*";
                sfd.Title = "Salvar Lista de Materiais";
                sfd.FileName = "BOM_Projeto_Fibra.csv";

                if (sfd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    ed.WriteMessage("\n[AVISO]: Exportação cancelada pelo usuário.");
                    return;
                }

                try
                {
                    using (var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        // Cabeçalho da Lista de Materiais
                        sw.WriteLine("--- LISTA DE MATERIAIS ---");
                        sw.WriteLine("Item;Quantidade;Unidade");

                        foreach (var kvp in blockCounts.OrderBy(x => x.Key))
                        {
                            sw.WriteLine($"{kvp.Key};{kvp.Value};UN");
                        }

                        foreach (var kvp in cableLengths.OrderBy(x => x.Key))
                        {
                            if (kvp.Value > 0) sw.WriteLine($"{kvp.Key};{kvp.Value:F2};Metros");
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
        }

        private static HashSet<string> LoadIgnoredBlocks()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? path = PluginPaths.DataFile(IgnoreFileName);
            if (path == null || !File.Exists(path)) return names;

            foreach (string line in DataFiles.ReadAllLines(path))
            {
                string name = line.Trim();
                if (name.Length > 0 && !name.StartsWith("#")) names.Add(name);
            }
            return names;
        }
    }
}
