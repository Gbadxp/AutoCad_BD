using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.EditorInput;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Parâmetros de perda óptica lidos de Dados\perdas_opticas.csv (colunas: Parametro;Valor).
    /// Parâmetros ausentes na planilha usam os valores padrão abaixo.
    /// </summary>
    public class OpticalLossTable
    {
        public const string FileName = "perdas_opticas.csv";

        private static readonly Dictionary<string, double> Defaults = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["atenuacao_db_km"] = 0.35,        // Pior caso G.652D em 1310 nm
            ["perda_emenda_db"] = 0.1,
            ["perda_conector_db"] = 0.5,
            ["margem_seguranca_db"] = 3.0,
            ["classe_B+_min_db"] = 13,
            ["classe_B+_max_db"] = 28,
            ["classe_C+_min_db"] = 17,
            ["classe_C+_max_db"] = 32,
            ["splitter_1x2_db"] = 3.7,
            ["splitter_1x4_db"] = 7.3,
            ["splitter_1x8_db"] = 10.5,
            ["splitter_1x16_db"] = 13.7,
            ["splitter_1x32_db"] = 17.1,
            ["splitter_1x64_db"] = 21.0
        };

        private readonly Dictionary<string, double> _values;

        private OpticalLossTable(Dictionary<string, double> values)
        {
            _values = values;
        }

        public double this[string key] => _values.TryGetValue(key, out double v) ? v : Defaults[key];

        public double? Splitter(string ratio)
        {
            return _values.TryGetValue($"splitter_{ratio.Trim().ToLowerInvariant()}_db", out double v) ? v : null;
        }

        public static OpticalLossTable Load(Editor? ed = null)
        {
            var values = new Dictionary<string, double>(Defaults, StringComparer.OrdinalIgnoreCase);
            string? path = PluginPaths.DataFile(FileName);

            if (path == null || !File.Exists(path))
            {
                ed?.WriteMessage($"\n[AVISO]: '{FileName}' não encontrado na pasta Dados. Usando perdas padrão.");
                return new OpticalLossTable(values);
            }

            try
            {
                foreach (var (lineNumber, cols) in DataFiles.ReadRows(path))
                {
                    if (cols.Length < 2) continue;
                    if (DataFiles.TryParseNumber(cols[1], out double v)) values[cols[0]] = v;
                    else if (lineNumber > 1) ed?.WriteMessage($"\n[AVISO] {FileName}: linha {lineNumber} ignorada (valor '{cols[1]}' inválido).");
                }
            }
            catch (IOException ex)
            {
                ed?.WriteMessage($"\n[AVISO]: Não foi possível ler '{FileName}' ({ex.Message}). Usando perdas padrão.");
            }

            return new OpticalLossTable(values);
        }
    }
}
