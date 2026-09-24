using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    public class PoleInfo
    {
        public ObjectId Id { get; set; }
        public Point3d Position { get; set; }
        public string Number { get; set; } = "-";
        public string Name { get; set; } = "Poste";

        /// <summary>
        /// Esforço nominal em kgf. O nome traz o valor em daN, como na norma ("DT 11/200" = 200 daN = 204 kgf).
        /// Null se não informado.
        /// </summary>
        public double? NominalKgf => Poles.ParseNominalDaN(Name) / FiberSettings.KgfToDaN;
    }

    public static class Poles
    {
        /// <summary>Postes = blocos com atributo NÚMERO/NUMERO/ID (mesma regra usada desde a primeira versão).</summary>
        public static List<PoleInfo> Collect(Transaction tr, BlockTableRecord space)
        {
            var poles = new List<PoleInfo>();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br || br.AttributeCollection.Count == 0) continue;

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

        /// <summary>Próximo número livre, com base no maior número de poste já existente no desenho.</summary>
        public static int NextNumber(Transaction tr, BlockTableRecord space)
        {
            int max = Collect(tr, space).Select(p => ParseNumber(p.Number) ?? 0).DefaultIfEmpty(0).Max();
            return max + 1;
        }

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

        public static bool IsPoleBlockName(string blockName)
        {
            return blockName.IndexOf("POSTE", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
