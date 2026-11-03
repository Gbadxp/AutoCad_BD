using System.IO;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Textos fixos das folhas do Gerar Folhas: as notas técnicas (no quadro NOTAS TÉCNICAS, numeradas), editadas na aba
    /// Carimbo da Configuração de Projeto e gravadas em Documentos\Fiber Plugin\notas.txt (uma por linha; valem para
    /// todos os desenhos), e os dois avisos da margem esquerda (Lei 5.194/66 e propriedade dos desenhos).
    /// </summary>
    public static class SheetNotes
    {
        public const string FileName = "notas.txt";

        /// <summary>Notas que vêm com o plugin (as do modelo de prancha da empresa).</summary>
        public static readonly IReadOnlyList<string> Defaults = new[]
        {
            "As instalações executadas e materiais utilizados na obra estão de acordo com as atuais Normas Vigentes da Energisa e em " +
            "consonância com as NBR's 14039, 5410, 5419 e demais normas ABNT que norteiam a atividade do projeto em questão.",
            "Serão utilizados somente equipamentos e materiais homologados pela Distribuidora Energisa.",
            "A aprovação da vistoria pela ENERGISA referente à obra deste projeto fica condicionada à apresentação da ART (Anotação de " +
            "Responsabilidade Técnica) de execução retirada no CREA local.",
            "Em caso de travessia sobre estradas, rodovias estaduais e federais ou sobre rios navegáveis, bem como a utilização de faixa " +
            "de domínio, os órgãos responsáveis serão consultados.",
            "Os colaboradores em campo para execução da obra estarão munidos de todos os equipamentos de proteção e em conformidade com " +
            "as NR 06, NR 10, NR 16 e demais que se aplicam à atividade."
        };

        /// <summary>Aviso da margem esquerda, em cima (Art. 18 da Lei Federal 5.194/66).</summary>
        public const string ChangesNotice =
            "Conforme Art. 18 da Lei Federal 5.194/66: \"As alterações do projeto ou plano original só poderão ser feitas pelo " +
            "profissional que o tenha elaborado.\" Portanto, toda e qualquer alteração técnica deste projeto deve ser solicitada ao " +
            "Coordenador Responsável pela execução, o qual deverá aprovar com o projetista antes da sua realização.";

        /// <summary>Aviso da margem esquerda, embaixo (propriedade dos desenhos).</summary>
        public const string PropertyNotice =
            "Os desenhos nesta prancha são de nossa propriedade, sendo proibido cópias, ampliações, reproduções totais e parciais ou " +
            "ceder a terceiros, salvo com autorização expressa de acordo com a legislação brasileira em vigor.";

        private static string FilePath => DataFiles.UserFile(FileName);

        /// <summary>As notas gravadas (sem a numeração, que a folha põe) ou, sem o arquivo, as que vêm com o plugin.</summary>
        public static List<string> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return Defaults.ToList();
                return Parse(string.Join("\n", DataFiles.ReadAllLines(FilePath).Where(l => !l.TrimStart().StartsWith("#"))));
            }
            catch (IOException) { return Defaults.ToList(); }
            catch (UnauthorizedAccessException) { return Defaults.ToList(); }
        }

        /// <summary>Uma nota por linha; linhas vazias saem e a numeração digitada ("1.", "2)") também.</summary>
        public static List<string> Parse(string text) =>
            text.Replace("\r", "").Split('\n')
                .Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"^\d{1,2}\s*[.)-]\s*", ""))
                .Where(l => l.Length > 0)
                .ToList();

        public static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.SequenceEqual(b);

        /// <summary>Grava as notas. Retorna o erro (null se gravou).</summary>
        public static string? Save(IEnumerable<string> notes)
        {
            var sb = new StringBuilder("# Fiber Plugin - notas técnicas das folhas do Gerar Folhas: uma por linha (a folha numera)\r\n");
            foreach (string note in notes) sb.Append(note).Append("\r\n");
            return DataFiles.Save(FileName, sb.ToString(), FileName);
        }
    }
}
