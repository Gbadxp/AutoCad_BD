using System.IO;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Dados da empresa usados no Memorial Descritivo e na Tabela A do relatório, editados na janela Configurações (aba
    /// Empresa) e gravados em Documentos\Fiber Plugin\empresa.txt (uma linha por campo, "Campo: valor"). Sem esse arquivo,
    /// vale o antigo Dados\empresa.txt (até a 1.9.35). Campo vazio simplesmente não aparece no documento.
    /// RG, CPF e endereço do representante são dados pessoais: ficam só nesse arquivo local.
    /// </summary>
    public sealed class CompanyInfo
    {
        public const string FileName = "empresa.txt";
        private const string LegacyFileName = "empresa.txt"; // Dentro da pasta Dados

        /// <summary>Campos na ordem da janela, em seções.</summary>
        public static readonly (string Section, string[] Fields)[] Layout =
        {
            ("Empresa", new[]
            {
                "Razão social", "Nome na plaqueta", "CNPJ", "Endereço", "Cidade", "CEP",
                "Telefone", "Telefone de emergência", "E-mail", "Tipo de companhia"
            }),
            ("Representante legal", new[] { "Representante", "Qualificação", "CREA", "RG", "CPF", "Endereço do representante" }),
            ("Concessionária", new[] { "Concessionária", "Departamento", "Aos cuidados de" })
        };

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        /// <summary>Valor do campo pelo nome da janela ("Razão social"); "" se vazio.</summary>
        public string this[string field]
        {
            get => _values.TryGetValue(DataFiles.FieldKey(field), out string? v) ? v : "";
            set => _values[DataFiles.FieldKey(field)] = (value ?? "").Trim();
        }

        public string LegalName => this["Razão social"];
        public string PlateName => this["Nome na plaqueta"];       // Nome na plaqueta de identificação
        public string Address => this["Endereço"];
        public string City => this["Cidade"];
        public string ZipCode => this["CEP"];
        public string Cnpj => this["CNPJ"];
        public string Phone => this["Telefone"];
        public string Email => this["E-mail"];
        public string EmergencyPhone => this["Telefone de emergência"];
        public string CompanyType => this["Tipo de companhia"] is { Length: > 0 } type ? type : "Internet"; // Tabela A
        public string Representative => this["Representante"];
        public string Qualification => this["Qualificação"];       // Ex.: brasileiro, casado, engenheiro
        public string Rg => this["RG"];
        public string Cpf => this["CPF"];
        public string RepresentativeAddress => this["Endereço do representante"];
        public string Crea => this["CREA"];
        public string Utility => this["Concessionária"];
        public string Department => this["Departamento"];
        public string Attention => this["Aos cuidados de"];       // Aos cuidados de

        /// <summary>Sem a razão social não há memorial.</summary>
        public bool IsEmpty => LegalName.Length == 0;

        /// <summary>Dados gravados (vazios se ainda não foram preenchidos), sem depender do AutoCAD.</summary>
        public static CompanyInfo Read()
        {
            var info = new CompanyInfo();
            string? path = DataFiles.Source(FileName, LegacyFileName);
            if (path == null) return info;
            try
            {
                foreach (var pair in DataFiles.ReadFields(path)) info._values[pair.Key] = pair.Value;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return info;
        }

        /// <summary>Dados para os documentos. Null (com o motivo em <paramref name="error"/>) se a empresa não foi preenchida.</summary>
        public static CompanyInfo? Load(out string? error)
        {
            CompanyInfo info = Read();
            error = info.IsEmpty ? "Os dados da empresa ainda não foram preenchidos (Configurações > Empresa)." : null;
            return info.IsEmpty ? null : info;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("# Dados da empresa do Fiber Plugin (Memorial Descritivo e Tabela A), editados na janela Configurações.\r\n");
            sb.Append("# RG, CPF e endereço do representante são dados pessoais: este arquivo fica só neste computador.\r\n");
            foreach (var (section, fields) in Layout)
            {
                sb.Append("\r\n# ").Append(section).Append("\r\n");
                foreach (string field in fields) sb.Append(field).Append(": ").Append(this[field]).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Grava os dados da empresa. Retorna o erro (null se gravou).</summary>
        public string? Save() => DataFiles.Save(FileName, ToText(), LegacyFileName);

        public bool SameAs(CompanyInfo other) => ToText() == other.ToText();
    }
}
