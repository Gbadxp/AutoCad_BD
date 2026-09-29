using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>Uma não conformidade com a NDU 009 encontrada no desenho.</summary>
    public sealed class NormIssue
    {
        public const string Error = "ERRO";
        public const string Warning = "AVISO";

        public string Severity { get; set; } = Warning;
        public string Where { get; set; } = "";      // Poste, caixa ou vão
        public Point3d Point { get; set; }
        public string Message { get; set; } = "";
        public string Rule { get; set; } = "";       // Item da NDU 009
    }

    /// <summary>
    /// Conferência do projeto com as regras da NDU 009 (Energisa) que dá para verificar pelo desenho:
    /// esforço acima do nominal, cabo em poste com equipamento da Energisa, CTO/CEO por poste, em esquina ou
    /// junto a equipamento, altura do cabo ao solo no meio do vão, vãos longos e massa/diâmetro por ponto de fixação.
    /// </summary>
    public static class NormCheck
    {
        /// <summary>Equipamentos que impedem qualquer ocupação do poste (NDU 009, item 8, nota I).</summary>
        private static readonly string[] BlockingEquipment = { "TRAFO", "TRANSFORMADOR", "CHAVE", "RELIGADOR", "REGULADOR", "CAPACITOR" };

        /// <summary>Além desses, caixas não podem ficar em poste com para-raios (item 17.5 c).</summary>
        private static readonly string[] BoxBlockingEquipment = { "PARA RAIO", "PARARRAIO", "PARA RAIOS" };

        public const double CornerDeflectionDeg = 45;      // Deflexão a partir da qual o poste é tratado como de esquina
        public const double LongSpanM = 60;                // Acima disso, considerar vento e temperatura
        public const double MaxMassKgKm = 1680;            // Por ponto de fixação (item 8 i)
        public const double MaxDiameterMm = 65;            // Conjunto de cabos por ponto de fixação (item 8 i)

        public static List<NormIssue> Run(ProjectData project)
        {
            var issues = new List<NormIssue>();
            CalcSettings settings = project.Traction.Settings;
            var occupied = project.Occupied.ToDictionary(o => o.Pole.Id, o => o.Result);

            // Equipamento elétrico de cada poste (o bloco vai no poste mais próximo)
            var equipment = new Dictionary<Autodesk.AutoCAD.DatabaseServices.ObjectId, List<string>>();
            foreach (var (name, position) in project.Equipment)
            {
                PoleInfo? pole = Poles.Nearest(project.PoleList, position, FiberSettings.PoleLinkRadius);
                if (pole == null) continue;
                if (!equipment.TryGetValue(pole.Id, out List<string>? list)) equipment[pole.Id] = list = new List<string>();
                list.Add(name);
            }

            // 1. Esforço acima do nominal (item 8.1)
            foreach (EffortPoint e in project.Efforts.Where(e => e.Load.Exceeded))
            {
                issues.Add(new NormIssue
                {
                    Severity = NormIssue.Error,
                    Where = e.Pole?.Number ?? "Ponto sem poste",
                    Point = e.Marker.Point,
                    Message = $"Esforço de {e.Load.TotalKgf:F1} kgf acima do nominal de {e.Load.NominalKgf:F0} kgf ({e.Load.Usage:F0}%)",
                    Rule = "8.1"
                });
            }

            // 2. Cabo em poste com equipamento da Energisa (item 8, nota I)
            foreach (var (pole, _) in project.Occupied)
            {
                string? blocking = Find(equipment, pole, BlockingEquipment);
                if (blocking == null) continue;
                issues.Add(new NormIssue
                {
                    Severity = NormIssue.Error,
                    Where = pole.Number,
                    Point = pole.Position,
                    Message = $"Cabo em poste com {blocking}: ocupação vetada",
                    Rule = "8 nota I"
                });
            }

            // 3. CTO/CEO: uma de cada por poste, fora de esquina e de poste com equipamento (itens 17.4 e 17.5)
            var polesById = project.PoleList.ToDictionary(p => p.Id.Handle.ToString());
            foreach (var group in project.BoxList.Where(b => b.Data.PoleHandle.Length > 0).GroupBy(b => b.Data.PoleHandle))
            {
                if (!polesById.TryGetValue(group.Key, out PoleInfo? pole)) continue;
                foreach (var kind in group.GroupBy(b => b.Data.Kind).Where(k => k.Count() > 1))
                {
                    issues.Add(new NormIssue
                    {
                        Severity = NormIssue.Error,
                        Where = pole.Number,
                        Point = pole.Position,
                        Message = $"{kind.Count()} {kind.Key} no mesmo poste ({string.Join(", ", kind.Select(b => b.Data.Id))}): máximo de uma por ocupante",
                        Rule = kind.Key == BlockCategories.Cto ? "17.4 nota IV" : "17.5 g"
                    });
                }

                string ids = string.Join(", ", group.Select(b => b.Data.Id));
                string? blocking = Find(equipment, pole, BlockingEquipment.Concat(BoxBlockingEquipment));
                if (blocking != null)
                {
                    issues.Add(new NormIssue
                    {
                        Severity = NormIssue.Error,
                        Where = pole.Number,
                        Point = pole.Position,
                        Message = $"{ids} em poste com {blocking}: caixas vetadas",
                        Rule = "17.5 c"
                    });
                }
                if (occupied.TryGetValue(pole.Id, out EffortResult? result) && result.MaxDeflectionDeg >= CornerDeflectionDeg)
                {
                    issues.Add(new NormIssue
                    {
                        Severity = NormIssue.Warning,
                        Where = pole.Number,
                        Point = pole.Position,
                        Message = $"{ids} em poste com deflexão de {result.MaxDeflectionDeg:F0}°: confira se é esquina (caixas vetadas em esquina)",
                        Rule = "17.5 c"
                    });
                }
            }

            // 4. Massa e diâmetro dos cabos por ponto de fixação (item 8 i)
            var models = project.Runs.GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.First().Model);
            foreach (var (pole, result) in project.Occupied)
            {
                var cables = result.Cables.Distinct().Where(models.ContainsKey).Select(n => models[n]).ToList();
                double mass = cables.Sum(c => c.WeightKgKm);
                if (mass > MaxMassKgKm)
                {
                    issues.Add(new NormIssue
                    {
                        Severity = NormIssue.Error,
                        Where = pole.Number,
                        Point = pole.Position,
                        Message = $"Cabos somam {mass:F0} kg/km no ponto de fixação (máximo {MaxMassKgKm:F0} kg/km)",
                        Rule = "8 i"
                    });
                }
                if (cables.Count > 0 && cables.All(c => c.DiameterMm != null) && cables.Sum(c => c.DiameterMm!.Value) > MaxDiameterMm)
                {
                    issues.Add(new NormIssue
                    {
                        Severity = NormIssue.Error,
                        Where = pole.Number,
                        Point = pole.Position,
                        Message = $"Conjunto de cabos com {cables.Sum(c => c.DiameterMm!.Value):F0} mm no ponto de fixação (máximo {MaxDiameterMm:F0} mm)",
                        Rule = "8 i"
                    });
                }
            }

            // 5. Vãos: altura ao solo no meio do vão com flecha de 1% (Tabela 02) e vãos longos (item 16.3)
            double maxSpan = (settings.AttachHeightM - settings.MinGroundClearanceM) / FiberSettings.SagRatio;
            if (maxSpan <= 0)
            {
                issues.Add(new NormIssue
                {
                    Severity = NormIssue.Error,
                    Where = "Parâmetros",
                    Message = $"Altura de fixação ({settings.AttachHeightM:0.00} m) não supera a altura mínima ao solo ({settings.MinGroundClearanceM:0.0} m)",
                    Rule = "Tabela 02"
                });
            }
            foreach (CableRun run in project.Runs)
            {
                for (int i = 0; i < run.Vertices.Count - 1; i++)
                {
                    Point3d a = run.Vertices[i], b = run.Vertices[i + 1];
                    double span = a.DistanceTo(b);
                    Point3d mid = new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0);
                    string where = $"Vão de {span:F1} m ({run.Name})";

                    if (maxSpan > 0 && span > maxSpan)
                    {
                        double clearance = settings.AttachHeightM - FiberSettings.SagRatio * span;
                        issues.Add(new NormIssue
                        {
                            Severity = NormIssue.Error,
                            Where = where,
                            Point = mid,
                            Message = $"Com flecha de 1% o cabo fica a {clearance:0.00} m do solo (mínimo {settings.MinGroundClearanceM:0.0} m); vão máximo {maxSpan:F0} m",
                            Rule = "Tabela 02"
                        });
                    }
                    if (span > LongSpanM)
                    {
                        issues.Add(new NormIssue
                        {
                            Severity = NormIssue.Warning,
                            Where = where,
                            Point = mid,
                            Message = $"Vão acima de {LongSpanM:F0} m: considerar vento e temperatura no cálculo",
                            Rule = "16.3"
                        });
                    }
                }
            }

            // 6. Postes ocupados sem esforço calculado
            var withEffort = new HashSet<Autodesk.AutoCAD.DatabaseServices.ObjectId>(project.Efforts.Where(e => e.Pole != null).Select(e => e.Pole!.Id));
            int missing = project.Occupied.Count(o => !withEffort.Contains(o.Pole.Id));
            if (missing > 0)
            {
                issues.Add(new NormIssue
                {
                    Severity = NormIssue.Warning,
                    Where = "Projeto",
                    Message = $"{missing} poste(s) com cabo sem esforço calculado: rode o Esforço no Percurso",
                    Rule = "16.2 e"
                });
            }

            return issues
                .OrderBy(i => i.Severity == NormIssue.Error ? 0 : 1)
                .ThenBy(i => Poles.ParseNumber(i.Where) ?? int.MaxValue)
                .ToList();
        }

        /// <summary>Primeiro equipamento do poste cujo nome tem uma das palavras (null se nenhum).</summary>
        private static string? Find(Dictionary<Autodesk.AutoCAD.DatabaseServices.ObjectId, List<string>> equipment, PoleInfo pole, IEnumerable<string> words)
        {
            if (!equipment.TryGetValue(pole.Id, out List<string>? names)) return null;
            return names.FirstOrDefault(n =>
            {
                // "Trafo com Chave FU" → " TRAFO COM CHAVE FU ": cada palavra buscada no início de uma palavra do nome
                string normalized = " " + System.Text.RegularExpressions.Regex.Replace(BlockCategories.Normalize(n), "[^A-Z0-9]+", " ") + " ";
                return words.Any(w => normalized.Contains(" " + w));
            });
        }
    }
}
