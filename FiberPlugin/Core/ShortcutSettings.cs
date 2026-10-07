using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Atalhos dos comandos do plugin, como os do AutoCAD (L = LINHA, CO = COPIAR): digita-se o atalho na linha de comando
    /// e tecla Enter ou Espaço. Os padrões começam com F (de Fibra) e não coincidem com os atalhos do acad.pgp original.
    /// O usuário muda na janela Configurações (aba Atalhos); fica em Documentos\Fiber Plugin\atalhos.txt
    /// ("FIBRA_LANCAR_CABO: FLC"; vazio = sem atalho). Comando que não está no arquivo usa o padrão.
    /// </summary>
    public static class ShortcutSettings
    {
        public const string FileName = "atalhos.txt";
        public const int MaxLength = 15;
        private static readonly Regex ValidAlias = new Regex("^[A-Z][A-Z0-9]*$");

        /// <summary>Comando → atalho padrão ("" = sem atalho até o usuário escolher).</summary>
        public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FIBRA"] = "FF",
            ["FIBRA_LANCAR_CABO"] = "FLC",
            ["FIBRA_ROTEAMENTO_AUTO"] = "FRA",
            ["FIBRA_INSERIR_POSTE"] = "FPO",
            ["FIBRA_INSERIR_CTO"] = "FCT",
            ["FIBRA_INSERIR_CEO"] = "FCE",
            ["FIBRA_INSERIR_ELETRICOS"] = "FEL",
            ["FIBRA_INSERIR_AMARRACAO"] = "FAM",
            ["FIBRA_RENUMERAR"] = "FRN",
            ["FIBRA_TAMANHO_BLOCO"] = "FTB",
            ["FIBRA_ESFORCO_TOTAL"] = "FET",
            ["FIBRA_ESFORCO_PERCURSO"] = "FEP",
            ["FIBRA_PARAMETROS"] = "FPA",
            ["FIBRA_ESFORCO_EXISTENTE"] = "FEX",
            ["FIBRA_VERIFICAR"] = "FVP",
            ["FIBRA_RELATORIO"] = "FRE",
            ["FIBRA_MEMORIAL"] = "FMD",
            ["FIBRA_MEMORIAL_ESFORCO"] = "FME",
            ["FIBRA_COORDENADAS_POSTES"] = "FCO",
            ["FIBRA_NORMA"] = "FNO",
            ["FIBRA_GERAR_FOLHAS"] = "FGF",
            ["FIBRA_IMPORTAR_KML"] = "FIK",
            ["FIBRA_EXPORTAR_KML"] = "FEK",
            ["FIBRA_IMPORTAR_RUAS"] = "FIR",
            ["FIBRA_CONFIGURACOES"] = "FCF",
            ["FIBRA_CALCULAR_ESFORCO"] = "FCA",
            ["FIBRA_ID_ENERGISA"] = "FID",
            ["FIBRA_ZONA_UTM"] = "FZU",
            ["FIBRA_ESCALA"] = "FES",
            ["FIBRA_ABRIR_PASTA"] = "FAP",
            ["FIBRA_ATUALIZAR_BLOCOS"] = "FAB",
            ["FIBRA_EXPORTAR_BLOCOS"] = "FXB",
            ["FIBRA_SOBRE"] = "FSO",
            ["FIBRA_RIBBON"] = ""
        };

        public static string FilePath => Path.Combine(PluginPaths.UserRoot, FileName);

        /// <summary>Atalho de cada comando: o do arquivo ou, sem ele, o padrão.</summary>
        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map = Defaults.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> saved;
            try
            {
                if (!File.Exists(FilePath)) return map;
                saved = DataFiles.ReadFields(FilePath);
            }
            catch (IOException) { return map; }
            catch (UnauthorizedAccessException) { return map; }

            foreach (string command in Defaults.Keys)
            {
                if (!saved.TryGetValue(DataFiles.FieldKey(command), out string? alias)) continue;
                alias = Normalize(alias);
                if (alias.Length == 0 || SyntaxError(alias) == null) map[command] = alias;
            }
            return map;
        }

        /// <summary>Grava os atalhos. Retorna o erro (null se gravou).</summary>
        public static string? Save(IDictionary<string, string> map)
        {
            var sb = new StringBuilder();
            sb.Append("# Fiber Plugin - atalhos dos comandos (use a aba Atalhos do botão Configurações para mudar).\r\n");
            sb.Append("# Comando: atalho. Vazio = sem atalho.\r\n");
            foreach (string command in Defaults.Keys)
            {
                sb.Append(command).Append(": ").Append(map.TryGetValue(command, out string? alias) ? Normalize(alias) : "").Append("\r\n");
            }
            try
            {
                Directory.CreateDirectory(PluginPaths.UserRoot);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
                return null;
            }
            catch (IOException ex) { return $"{FileName}: {ex.Message}"; }
            catch (UnauthorizedAccessException ex) { return $"{FileName}: {ex.Message}"; }
        }

        /// <summary>Atalho como o AutoCAD usa: sem espaços e em maiúsculas.</summary>
        public static string Normalize(string? alias) => (alias ?? "").Trim().ToUpperInvariant();

        /// <summary>Problema na forma do atalho (null se estiver certo). Vazio é válido: sem atalho.</summary>
        public static string? SyntaxError(string alias)
        {
            if (alias.Length == 0) return null;
            if (alias.Length > MaxLength) return $"no máximo {MaxLength} caracteres";
            if (!ValidAlias.IsMatch(alias)) return "só letras e números, começando por letra";
            return null;
        }

        /// <summary>Mesmos atalhos (para saber se algo mudou na janela).</summary>
        public static bool Same(IDictionary<string, string> a, IDictionary<string, string> b) =>
            Defaults.Keys.All(k => Normalize(a.TryGetValue(k, out string? x) ? x : "") == Normalize(b.TryGetValue(k, out string? y) ? y : ""));
    }
}
