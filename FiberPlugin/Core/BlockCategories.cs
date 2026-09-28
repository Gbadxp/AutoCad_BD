using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Grupo de cada bloco do BLOCOS.dwg, pelo nome. Cada grupo tem o seu comando de inserir:
    /// Postes (POSTE DT-DUPLO T, POSTE CC-CIRCULAR), CTO, CEO (CEO_1, CEO_2), Elétrica (TRAFO, CHAVE FU,
    /// PARA-RAIO...), Amarração e Outros (o que não se encaixar em nenhum grupo).
    /// </summary>
    public static class BlockCategories
    {
        public const string Poles = "Postes";
        public const string Cto = "CTO";
        public const string Ceo = "CEO";
        public const string Electrical = "Elétrica";
        public const string Anchoring = "Amarração";
        public const string Others = "Outros";

        /// <summary>Ordem das categorias na janela de escolha de blocos.</summary>
        public static readonly string[] Order = { Poles, Cto, Ceo, Electrical, Anchoring, Others };

        private static readonly string[] ElectricalWords =
            { "TRAFO", "TRANSFORMADOR", "CHAVE", "ATERRAMENTO", "PARA-RAIO", "PARA RAIO", "PARARRAIO", "LUMINARIA", "MEDIDOR" };

        public static string Of(string blockName)
        {
            string name = Normalize(blockName);
            HashSet<string> words = Words(name);

            if (name.Contains("POSTE") || words.Contains("DT") || words.Contains("CC")) return Poles;
            if (words.Contains("CTO")) return Cto;
            if (words.Contains("CEO")) return Ceo;
            if (name.Contains("AMARRA")) return Anchoring;
            if (ElectricalWords.Any(name.Contains)) return Electrical;
            return Others;
        }

        /// <summary>Blocos da biblioteca de uma categoria (sem a seta de esforço, que é colocada pelos cálculos).</summary>
        public static List<BlockEntry> Blocks(string category)
        {
            return BlockRepository.List()
                .Where(b => b.Category == category)
                .Where(b => !b.Name.Equals(FiberSettings.EffortBlockName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Maiúsculas e sem acentos ("Amarração" → "AMARRACAO").</summary>
        private static string Normalize(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text.ToUpperInvariant().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString();
        }

        private static HashSet<string> Words(string normalized)
        {
            return new HashSet<string>(Regex.Split(normalized, "[^A-Z0-9]+").Where(w => w.Length > 0));
        }
    }
}
