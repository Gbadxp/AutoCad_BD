using System.Globalization;
using System.IO;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Arquivos de dados do plugin (cabos, postes, tração, empresa) em Documentos\Fiber Plugin, gravados pela janela
    /// Configurações: texto com ";" entre as colunas e vírgula decimal, ou "Campo: valor". Sem o arquivo, vale o padrão
    /// que vem no código. Até a 1.9.35 esses dados eram planilhas da pasta Dados (cabos.csv...): elas são lidas enquanto
    /// o arquivo novo não existe e, ao gravar o novo, vão para Dados\Antigos.
    /// Aceita também arquivos salvos pelo Excel em pt-BR (codificação ANSI) ou em UTF-8.
    /// </summary>
    public static class DataFiles
    {
        /// <summary>Números gravados nos arquivos: vírgula decimal, como no resto das janelas.</summary>
        public static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

        public const string LegacyFolderName = "Antigos";

        /// <summary>Caminho do arquivo de dados do plugin (Documentos\Fiber Plugin\nome).</summary>
        public static string UserFile(string fileName) => Path.Combine(PluginPaths.UserRoot, fileName);

        /// <summary>
        /// Arquivo de onde ler: o do plugin; sem ele, a planilha antiga da pasta Dados (<paramref name="legacyName"/>).
        /// Null = nenhum dos dois (usar o padrão do código).
        /// </summary>
        public static string? Source(string fileName, string legacyName)
        {
            string file = UserFile(fileName);
            if (File.Exists(file)) return file;
            string? legacy = PluginPaths.DataFile(legacyName);
            return legacy != null && File.Exists(legacy) ? legacy : null;
        }

        /// <summary>
        /// Grava o arquivo de dados do plugin (UTF-8) e tira a planilha antiga do caminho (vai para Dados\Antigos, onde
        /// fica como cópia). Retorna o erro (null se gravou).
        /// </summary>
        public static string? Save(string fileName, string content, string legacyName)
        {
            try
            {
                Directory.CreateDirectory(PluginPaths.UserRoot);
                File.WriteAllText(UserFile(fileName), content, new UTF8Encoding(true));
            }
            catch (IOException ex) { return $"{fileName}: {ex.Message}"; }
            catch (UnauthorizedAccessException ex) { return $"{fileName}: {ex.Message}"; }

            RetireLegacy(legacyName);
            return null;
        }

        /// <summary>Move a planilha antiga para Dados\Antigos (sem apagar nada). Se não der (aberta no Excel), fica onde está.</summary>
        private static void RetireLegacy(string legacyName)
        {
            string? legacy = PluginPaths.DataFile(legacyName);
            if (legacy == null || !File.Exists(legacy) || !PluginPaths.IsInstalled) return;
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(legacy)!, LegacyFolderName);
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, legacyName);
                if (File.Exists(target))
                    target = Path.Combine(folder, Path.GetFileNameWithoutExtension(legacyName) + DateTime.Now.ToString(" yyyy-MM-dd HH-mm-ss") + Path.GetExtension(legacyName));
                File.Move(legacy, target);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Lê todas as linhas, mesmo com o arquivo aberto no Excel.</summary>
        public static string[] ReadAllLines(string path)
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }

            string text;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            else
            {
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    // "CSV (separado por vírgulas)" do Excel é salvo em ANSI (Windows-1252)
                    text = Encoding.GetEncoding(28591).GetString(bytes); // Latin-1
                }
            }

            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        /// <summary>
        /// Linhas de dados já separadas em colunas. Ignora linhas vazias e comentários (#).
        /// Retorna junto o número da linha no arquivo para mensagens de erro.
        /// </summary>
        public static IEnumerable<(int LineNumber, string[] Columns)> ReadRows(string path)
        {
            string[] lines = ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                char separator = line.Contains(';') ? ';' : ',';
                string[] columns = line.Split(separator);
                for (int c = 0; c < columns.Length; c++) columns[c] = columns[c].Trim().Trim('"').Trim();

                yield return (i + 1, columns);
            }
        }

        /// <summary>
        /// Arquivo de campos, uma linha por campo no formato "Campo: valor" (empresa.txt, projeto.txt). Ignora linhas
        /// vazias e comentários (#). A chave é o nome do campo sem acentos, espaços e maiúsculas (veja FieldKey).
        /// </summary>
        public static Dictionary<string, string> ReadFields(string path)
        {
            var fields = new Dictionary<string, string>();
            foreach (string raw in ReadAllLines(path))
            {
                string line = raw.Trim();
                int colon = line.IndexOf(':');
                if (line.Length == 0 || line.StartsWith("#") || colon <= 0) continue;
                fields[FieldKey(line.Substring(0, colon))] = line.Substring(colon + 1).Trim();
            }
            return fields;
        }

        /// <summary>Nome do campo sem acentos, espaços e maiúsculas ("Endereço da obra" → "enderecodaobra").</summary>
        public static string FieldKey(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Converte número aceitando vírgula ou ponto como separador decimal.</summary>
        public static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
