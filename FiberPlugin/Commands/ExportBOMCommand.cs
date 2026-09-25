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
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);

                // Metragem por tipo de cabo, com a descrição completa do catálogo quando cadastrado
                foreach (var kvp in CadHelpers.CableLengths(tr, modelSpace))
                {
                    CableModel? model = CableProvider.Find(catalog, kvp.Key);
                    cableLengths[model != null ? $"Cabo {model.FullName} ({model.ShortName})" : $"Cabo {kvp.Key}"] = kvp.Value;
                }

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
                    // 2. Coordenadas de Pontos
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

            CadHelpers.SaveCsv(ed, "Salvar Lista de Materiais", "BOM_Projeto_Fibra.csv", sw =>
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
            });
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
