using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Leitura das planilhas da pasta Dados. Aceita arquivos salvos pelo Excel em pt-BR
    /// (separador ";" e vírgula decimal, codificação ANSI) ou em UTF-8.
    /// </summary>
    public static class DataFiles
    {
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

        /// <summary>Converte número aceitando vírgula ou ponto como separador decimal.</summary>
        public static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
