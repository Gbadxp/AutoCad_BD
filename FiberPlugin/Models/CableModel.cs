using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using FiberPlugin.Core;

namespace FiberPlugin.Models
{
    public class CableModel
    {
        public string FullName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public double WeightKgKm { get; set; }

        public override string ToString()
        {
            return FullName; // Para exibir no ComboBox do formulário
        }
    }

    /// <summary>
    /// Catálogo de cabos lido da planilha Dados\cabos.csv (colunas: NomeCompleto;NomeCurto;Peso_kg_km).
    /// A planilha é relida a cada comando, então basta salvar no Excel para o cabo novo aparecer.
    /// </summary>
    public static class CableProvider
    {
        public const string FileName = "cabos.csv";

        /// <summary>
        /// Cabos da planilha. Lista vazia (com aviso no Editor) se a planilha não existir ou não tiver
        /// nenhum cabo válido: a planilha é a única fonte dos cabos, não há lista paralela no código.
        /// </summary>
        public static List<CableModel> GetCables(Editor? ed = null)
        {
            string? path = PluginPaths.DataFile(FileName);
            if (path == null || !File.Exists(path))
            {
                ed?.WriteMessage($"\n[ERRO]: Planilha de cabos não encontrada ({path ?? PluginPaths.DataFolderName + "/" + FileName}). " +
                                 "Use FIBRA_ABRIR_PASTA para abrir a pasta de dados.");
                return new List<CableModel>();
            }

            try
            {
                var errors = new List<string>();
                List<CableModel> cables = Parse(path, errors);
                foreach (string error in errors) ed?.WriteMessage($"\n[AVISO] {FileName}: {error}");

                if (cables.Count == 0) ed?.WriteMessage($"\n[ERRO]: Nenhum cabo válido em '{path}'.");
                return cables;
            }
            catch (IOException ex)
            {
                ed?.WriteMessage($"\n[ERRO]: Não foi possível ler '{path}' ({ex.Message}).");
                return new List<CableModel>();
            }
        }

        public static CableModel? Find(IEnumerable<CableModel> cables, string? shortName)
        {
            if (shortName == null || shortName.Trim().Length == 0) return null;
            return cables.FirstOrDefault(c => c.ShortName.Equals(shortName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static List<CableModel> Parse(string path, List<string> errors)
        {
            var cables = new List<CableModel>();
            bool firstRow = true;

            foreach (var (lineNumber, cols) in DataFiles.ReadRows(path))
            {
                bool isFirst = firstRow;
                firstRow = false;

                if (cols.Length < 3)
                {
                    errors.Add($"linha {lineNumber} ignorada (esperado NomeCompleto;NomeCurto;Peso_kg_km).");
                    continue;
                }

                if (!DataFiles.TryParseNumber(cols[2], out double weight))
                {
                    // A primeira linha normalmente é o cabeçalho
                    if (!isFirst) errors.Add($"linha {lineNumber} ignorada (peso '{cols[2]}' inválido).");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cols[0]) || string.IsNullOrWhiteSpace(cols[1]) || weight <= 0)
                {
                    errors.Add($"linha {lineNumber} ignorada (nome vazio ou peso não positivo).");
                    continue;
                }

                if (Find(cables, cols[1]) != null)
                {
                    errors.Add($"linha {lineNumber} ignorada (nome curto '{cols[1]}' repetido).");
                    continue;
                }

                cables.Add(new CableModel { FullName = cols[0], ShortName = cols[1], WeightKgKm = weight });
            }

            return cables;
        }
    }
}
