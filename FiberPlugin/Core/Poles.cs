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

        /// <summary>"11/300" (altura em m / esforço em daN), o texto mostrado no desenho.</summary>
        public string HeightEffort =>
            HeightM.ToString("0.#", CultureInfo.InvariantCulture) + "/" + EffortDaN.ToString("0", CultureInfo.InvariantCulture);

        /// <summary>"DT 11/300", usado na listagem de postes.</summary>
        public string Designation => $"{Type} {HeightEffort}";

        public string TypeName => Type == Circular ? "Circular" : "Duplo T";

        /// <summary>Número formatado: P-01, P-02...</summary>
        public static string NumberText(int number) => "P-" + number.ToString("D2", CultureInfo.InvariantCulture);
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

        /// <summary>Texto de situação do poste comparando o esforço calculado com o nominal.</summary>
        public static string Status(double effortKgf, double? nominalKgf)
        {
            if (nominalKgf == null || nominalKgf <= 0) return "SEM NOMINAL";
            return effortKgf > nominalKgf ? "EXCEDIDO" : "OK";
        }

        public static bool IsExceeded(PoleInfo? pole, double effortKgf)
        {
            return Status(effortKgf, pole?.NominalKgf) == "EXCEDIDO";
        }

        /// <summary>
        /// Texto de situação usado na linha de comando, igual em todos os comandos de esforço:
        /// "DT 11/200: nominal 204 kgf → OK (12% de utilização)". Null se o poste não tem nominal.
        /// </summary>
        public static string? StatusText(PoleInfo? pole, double effortKgf)
        {
            double? nominal = pole?.NominalKgf;
            if (pole == null || nominal == null || nominal <= 0) return null;

            return $"{pole.Name}: nominal {nominal:F0} kgf → {Status(effortKgf, nominal)} " +
                   $"({effortKgf / nominal.Value * 100:F0}% de utilização)";
        }
    }
}
