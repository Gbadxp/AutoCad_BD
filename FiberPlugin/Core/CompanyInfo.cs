using System.Globalization;
using System.IO;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Dados da empresa usados no Memorial Descritivo, lidos de Dados\empresa.txt
    /// (uma linha por campo, no formato "Campo: valor"; linhas com # são comentários).
    /// Campo vazio simplesmente não aparece no documento.
    /// </summary>
    public sealed class CompanyInfo
    {
        public const string FileName = "empresa.txt";

        public string LegalName { get; private set; } = "";        // Razão social
        public string PlateName { get; private set; } = "";        // Nome na plaqueta de identificação
        public string Address { get; private set; } = "";
        public string City { get; private set; } = "";
        public string ZipCode { get; private set; } = "";
        public string Cnpj { get; private set; } = "";
        public string Phone { get; private set; } = "";
        public string Email { get; private set; } = "";
        public string EmergencyPhone { get; private set; } = "";
        public string CompanyType { get; private set; } = "";      // Tipo de companhia (Tabela A): Internet, Telefonia...
        public string Representative { get; private set; } = "";
        public string Qualification { get; private set; } = "";    // Ex.: brasileiro, casado, engenheiro
        public string Rg { get; private set; } = "";
        public string Cpf { get; private set; } = "";
        public string RepresentativeAddress { get; private set; } = "";
        public string Crea { get; private set; } = "";
        public string Utility { get; private set; } = "";          // Concessionária
        public string Department { get; private set; } = "";
        public string Attention { get; private set; } = "";        // Aos cuidados de

        /// <summary>Lê o arquivo. Null (com o motivo em <paramref name="error"/>) se ele não existir.</summary>
        public static CompanyInfo? Load(out string? error)
        {
            error = null;
            string? path = PluginPaths.DataFile(FileName);
            if (path == null || !File.Exists(path))
            {
                error = $"Arquivo com os dados da empresa não encontrado ({path ?? PluginPaths.DataFolderName + "/" + FileName}).";
                return null;
            }

            var fields = new Dictionary<string, string>();
            foreach (string raw in DataFiles.ReadAllLines(path))
            {
                string line = raw.Trim();
                int colon = line.IndexOf(':');
                if (line.Length == 0 || line.StartsWith("#") || colon <= 0) continue;
                fields[Key(line.Substring(0, colon))] = line.Substring(colon + 1).Trim();
            }

            string Get(string key) => fields.TryGetValue(Key(key), out string? value) ? value : "";
            return new CompanyInfo
            {
                LegalName = Get("Razão social"),
                PlateName = Get("Nome na plaqueta"),
                Address = Get("Endereço"),
                City = Get("Cidade"),
                ZipCode = Get("CEP"),
                Cnpj = Get("CNPJ"),
                Phone = Get("Telefone"),
                Email = Get("E-mail"),
                EmergencyPhone = Get("Telefone de emergência"),
                CompanyType = Get("Tipo de companhia") is { Length: > 0 } type ? type : "Internet",
                Representative = Get("Representante"),
                Qualification = Get("Qualificação"),
                Rg = Get("RG"),
                Cpf = Get("CPF"),
                RepresentativeAddress = Get("Endereço do representante"),
                Crea = Get("CREA"),
                Utility = Get("Concessionária"),
                Department = Get("Departamento"),
                Attention = Get("Aos cuidados de")
            };
        }

        /// <summary>Nome do campo sem acentos, espaços e maiúsculas ("Endereço" → "endereco").</summary>
        private static string Key(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
