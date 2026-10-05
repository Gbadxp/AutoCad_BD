using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>Dados gravados no bloco pelo FIBRA_INSERIR_POSTE.</summary>
    public class PoleData
    {
        public const string DoubleT = "DT";
        public const string Circular = "CC";

        public int Number { get; set; }
        public string Type { get; set; } = DoubleT;   // DT = Duplo T, CC = Circular
        public double HeightM { get; set; }
        public double EffortDaN { get; set; }         // Esforço nominal, em daN como na norma

        /// <summary>Esforço que já existe no poste (redes da Energisa e outras ocupantes), em kgf a 20 cm do topo.</summary>
        public double ExistingKgf { get; set; }

        /// <summary>ID do poste fornecido pela Energisa (ID_Poste da Tabela A da NDU 009). Vazio enquanto não informado.</summary>
        public string EnergisaId { get; set; } = "";

        /// <summary>"11/300" (altura em m / esforço em daN), o texto mostrado no desenho.</summary>
        public string HeightEffort =>
            HeightM.ToString("0.#", CultureInfo.InvariantCulture) + "/" + EffortDaN.ToString("0", CultureInfo.InvariantCulture);

        /// <summary>"DT 11/300", usado na listagem de postes.</summary>
        public string Designation => $"{Type} {HeightEffort}";

        public string TypeName => Type == Circular ? "Circular" : "Duplo T";

        /// <summary>Mesmo modelo de poste: mesmo tipo, altura e esforço (o número e os dados do desenho não contam).</summary>
        public bool SameModel(PoleData other) =>
            string.Equals(Type, other.Type, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(HeightM - other.HeightM) < 1e-6 && Math.Abs(EffortDaN - other.EffortDaN) < 1e-6;

        /// <summary>Número formatado: P-01, P-02... (prefixo e dígitos da janela Configurações).</summary>
        public static string NumberText(int number) => UserSettings.Current.Name(UserSettings.Current.PolePrefix, number);
    }

    public class PoleInfo
    {
        public ObjectId Id { get; set; }
        public Point3d Position { get; set; }
        public string Number { get; set; } = "-";
        public string Name { get; set; } = "Poste";

        /// <summary>Dados do FIBRA_INSERIR_POSTE (null em postes antigos, identificados só por atributo).</summary>
        public PoleData? Data { get; set; }

        /// <summary>
        /// Esforço nominal em kgf. Vem dos dados do poste ou do nome ("DT 11/200"); em ambos o valor está
        /// em daN, como na norma (200 daN = 204 kgf). Null se não informado.
        /// </summary>
        public double? NominalKgf => (Data?.EffortDaN ?? Poles.ParseNominalDaN(Name)) / FiberSettings.KgfToDaN;

        /// <summary>Altura do poste em m (dos dados ou do nome "DT 11/200"). Null se não informada.</summary>
        public double? HeightM => Data?.HeightM > 0 ? Data.HeightM : Poles.ParseHeight(Name);
    }

    /// <summary>
    /// Esforço do projeto num poste comparado com o nominal, como pede a NDU 009 (Anexo A, itens 7 e 11):
    /// a resultante na altura do cabo é transferida para 20 cm do topo, Ft = F × hc / h, com altura útil
    /// h = L − e e engastamento e = L/10 + 0,60 m, e somada ao esforço que já existe no poste.
    /// </summary>
    public sealed class PoleLoad
    {
        /// <summary>Resultante dos cabos do projeto na altura de fixação, em kgf.</summary>
        public double CableKgf { get; private set; }

        /// <summary>Resultante transferida a 20 cm do topo (null se a altura do poste é desconhecida).</summary>
        public double? TopKgf { get; private set; }

        public double ExistingKgf { get; private set; }
        public double? NominalKgf { get; private set; }

        /// <summary>Esforço do projeto referido ao topo (ou na altura do cabo, sem a altura do poste).</summary>
        public double ProjectKgf => TopKgf ?? CableKgf;

        /// <summary>Projeto + existente. A soma é escalar (pior caso: os dois esforços no mesmo sentido).</summary>
        public double TotalKgf => ProjectKgf + ExistingKgf;

        public double? Usage => NominalKgf > 0 ? TotalKgf / NominalKgf.Value * 100 : (double?)null;

        public string Result => NominalKgf is not > 0 ? "SEM NOMINAL" : TotalKgf > NominalKgf ? "EXCEDIDO" : "OK";
        public bool Exceeded => Result == "EXCEDIDO";

        public static PoleLoad For(PoleInfo? pole, double cableKgf, double attachHeightM)
        {
            var load = new PoleLoad { CableKgf = cableKgf, ExistingKgf = pole?.Data?.ExistingKgf ?? 0, NominalKgf = pole?.NominalKgf };
            if (pole?.HeightM is double length && length > 0)
            {
                double usefulHeight = length - (length / 10.0 + 0.60);
                if (usefulHeight > 0) load.TopKgf = cableKgf * attachHeightM / usefulHeight;
            }
            return load;
        }

        /// <summary>
        /// Texto da linha de comando, igual em todos os comandos de esforço:
        /// "DT 11/300: 14,20 kgf no topo + 0 existente = 14,20 de 306 kgf → OK (5%)". Null sem poste com nominal.
        /// </summary>
        public string? Text(PoleInfo? pole)
        {
            if (pole == null || NominalKgf is not > 0) return null;
            string top = TopKgf != null ? $"{TopKgf:F2} kgf a 20 cm do topo" : $"{CableKgf:F2} kgf (altura do poste desconhecida)";
            string existing = ExistingKgf > 0 ? $" + {ExistingKgf:F2} existente" : "";
            return $"{pole.Name}: {top}{existing} de {NominalKgf:F0} kgf nominal → {Result} ({Usage:F0}%)";
        }
    }

    public static class Poles
    {
        /// <summary>
        /// Postes = blocos inseridos pelo FIBRA_INSERIR_POSTE ou, nos desenhos antigos,
        /// blocos com atributo NÚMERO/NUMERO/ID.
        /// </summary>
        public static List<PoleInfo> Collect(Transaction tr, BlockTableRecord space)
        {
            var poles = new List<PoleInfo>();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br) continue;

                PoleData? data = XDataTags.ReadPole(br);
                if (data != null)
                {
                    poles.Add(new PoleInfo
                    {
                        Id = id,
                        Position = br.Position,
                        Number = PoleData.NumberText(data.Number),
                        Name = data.Designation,
                        Data = data
                    });
                    continue;
                }

                if (br.AttributeCollection.Count == 0) continue;

                string? number = CadHelpers.GetAttributeValue(tr, br, CadHelpers.NumberTags);
                if (number == null) continue;

                string? name = CadHelpers.GetAttributeValue(tr, br, CadHelpers.NameTags);
                poles.Add(new PoleInfo
                {
                    Id = id,
                    Position = br.Position,
                    Number = string.IsNullOrWhiteSpace(number) ? "-" : number,
                    Name = name == null || name.Trim().Length == 0 ? "Poste" : name
                });
            }
            return poles;
        }

        public static PoleInfo? Nearest(IEnumerable<PoleInfo> poles, Point3d point, double tolerance)
        {
            return poles
                .Where(p => p.Position.DistanceTo(point) <= tolerance)
                .OrderBy(p => p.Position.DistanceTo(point))
                .FirstOrDefault();
        }

        /// <summary>Extrai o número do poste ("N° 12" → 12).</summary>
        public static int? ParseNumber(string text)
        {
            string digits = new string(text.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int n) ? n : null;
        }

        /// <summary>Números de poste já usados no desenho.</summary>
        public static HashSet<int> UsedNumbers(Transaction tr, BlockTableRecord space)
        {
            return new HashSet<int>(Collect(tr, space).Select(p => ParseNumber(p.Number) ?? 0));
        }

        /// <summary>Primeiro número a partir de <paramref name="from"/> que ainda não foi usado.</summary>
        public static int NextFree(HashSet<int> used, int from)
        {
            int n = Math.Max(1, from);
            while (used.Contains(n)) n++;
            return n;
        }

        /// <summary>Pergunta o número do próximo poste. Null se o usuário cancelar.</summary>
        /// <param name="what">O que está sendo numerado, com o artigo: "do próximo poste", "da próxima CTO".</param>
        public static int? AskNumber(Autodesk.AutoCAD.EditorInput.Editor ed, int suggested, string what = "do próximo poste") =>
            CadHelpers.AskInt(ed, $"\nNúmero {what} <{suggested}>: ", suggested);

        /// <summary>"DT 11/200" ou "CC 12/600daN" → 200 / 600.</summary>
        public static double? ParseNominalDaN(string name)
        {
            Match m = Regex.Match(name, @"\d+(?:[.,]\d+)?\s*/\s*(\d+(?:[.,]\d+)?)");
            if (!m.Success) return null;
            return double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        }

        /// <summary>"DT 11/200" ou "CC 12/600" → 11 / 12 (altura, antes da barra).</summary>
        public static double? ParseHeight(string name)
        {
            Match m = Regex.Match(name, @"(\d+(?:[.,]\d+)?)\s*/\s*\d");
            if (!m.Success) return null;
            return double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        }
    }
}
