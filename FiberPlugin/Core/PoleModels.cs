using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.EditorInput;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Modelos de poste oferecidos no FIBRA_INSERIR_POSTE, lidos de Dados\postes.csv
    /// (colunas: Tipo;Altura_m;Esforco_daN, com tipo DT = Duplo T ou CC = Circular).
    /// Cada tipo usa o seu bloco do BLOCOS.dwg.
    /// </summary>
    public static class PoleModels
    {
        public const string FileName = "postes.csv";

        public static List<PoleData> Load(Editor? ed = null)
        {
            var models = new List<PoleData>();
            string? path = PluginPaths.DataFile(FileName);
            if (path == null || !File.Exists(path))
            {
                ed?.WriteMessage($"\n[ERRO]: Planilha de postes não encontrada ({path ?? PluginPaths.DataFolderName + "/" + FileName}).");
                return models;
            }

            try
            {
                foreach (var (lineNumber, cols) in DataFiles.ReadRows(path))
                {
                    if (cols.Length < 3) continue;

                    string type = cols[0].Trim().ToUpperInvariant();
                    bool validNumbers = DataFiles.TryParseNumber(cols[1], out double height) &
                                        DataFiles.TryParseNumber(cols[2], out double effort);

                    if (!validNumbers)
                    {
                        if (lineNumber > 1) ed?.WriteMessage($"\n[AVISO] {FileName}: linha {lineNumber} ignorada (altura ou esforço inválido).");
                        continue;
                    }
                    if (type != PoleData.DoubleT && type != PoleData.Circular)
                    {
                        ed?.WriteMessage($"\n[AVISO] {FileName}: linha {lineNumber} ignorada (tipo '{cols[0]}': use DT ou CC).");
                        continue;
                    }

                    models.Add(new PoleData { Type = type, HeightM = height, EffortDaN = effort });
                }
            }
            catch (IOException ex)
            {
                ed?.WriteMessage($"\n[ERRO]: Não foi possível ler '{path}' ({ex.Message}).");
            }

            return models;
        }

        /// <summary>
        /// Bloco da biblioteca para o tipo de poste. Ordem de procura:
        /// nome exatamente "DT"/"CC"; nome com DT/CC como palavra (ex.: "POSTE DT"); "DUPLO T" / "CIRCULAR".
        /// </summary>
        public static string? BlockFor(string type, IEnumerable<string> blockNames)
        {
            List<string> names = blockNames.ToList();
            string fullName = type == PoleData.Circular ? "CIRCULAR" : "DUPLO";

            return names.FirstOrDefault(n => n.Trim().Equals(type, StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault(n => Words(n).Contains(type))
                ?? names.FirstOrDefault(n => Words(n).Contains(fullName));
        }

        private static HashSet<string> Words(string name)
        {
            return new HashSet<string>(Regex.Split(name.ToUpperInvariant(), "[^A-Z0-9]+").Where(w => w.Length > 0));
        }
    }
}
