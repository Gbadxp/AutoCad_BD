using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>Um cabo desenhado, com o peso vindo do catálogo.</summary>
    public class CableRun
    {
        public string Name { get; set; } = "";
        public double WeightKgKm { get; set; }
        public List<Point3d> Vertices { get; set; } = new List<Point3d>();
    }

    public class EffortResult
    {
        public Vector2d Resultant { get; set; } = new Vector2d(0, 0);
        public int CableCount { get; set; }
        public List<string> Cables { get; } = new List<string>();

        /// <summary>Esforço resultante em kgf.</summary>
        public double Kgf => Resultant.Length;

        /// <summary>Ângulo da resultante em radianos (0 a 2π). Zero quando o esforço é desprezível.</summary>
        public double AngleRad => Kgf > 0.1 ? Resultant.Angle : 0;
        public double AngleDeg => AngleRad * 180.0 / Math.PI;

        /// <summary>Cabos que terminam no poste (ancoragem) e que passam por ele.</summary>
        public int EndCount { get; set; }
        public int PassCount { get; set; }

        /// <summary>Maior deflexão (mudança de direção) entre os vãos de um cabo que passa pelo poste, em graus.</summary>
        public double MaxDeflectionDeg { get; set; }

        /// <summary>Deflexão a partir da qual o poste é considerado "de ângulo".</summary>
        public const double AngleThresholdDeg = 5;

        /// <summary>Situação do poste no percurso: "Fim de rede", "Ângulo (35°)" ou "Passagem".</summary>
        public string Situation =>
            EndCount > 0 ? "Fim de rede"
            : MaxDeflectionDeg >= AngleThresholdDeg ? $"Ângulo ({MaxDeflectionDeg:F0}°)"
            : "Passagem";

        /// <summary>Registra a geometria do cabo no poste: ponta de rede ou passagem com deflexão.</summary>
        internal void AddGeometry(Point3d at, Point3d? previous, Point3d? next)
        {
            if (previous == null || next == null)
            {
                EndCount++;
                return;
            }

            PassCount++;
            var a = new Vector2d(previous.Value.X - at.X, previous.Value.Y - at.Y);
            var b = new Vector2d(next.Value.X - at.X, next.Value.Y - at.Y);
            if (a.Length < 1e-9 || b.Length < 1e-9) return;

            // Vãos alinhados formam 180°; a deflexão é o quanto falta para isso
            double inner = a.GetAngleTo(b) * 180.0 / Math.PI;
            MaxDeflectionDeg = Math.Max(MaxDeflectionDeg, 180.0 - inner);
        }
    }

    /// <summary>
    /// Esforço resultante no poste pela soma vetorial das trações dos vãos que chegam nele.
    ///
    /// Premissas (simplificadas, documentadas no README):
    ///  - Tração de cada vão pela aproximação da parábola: T = p·L² / (8·f), com flecha f = 1% do vão
    ///    → T = 12,5 · p · L  (p em kgf/m, resultado em kgf).
    ///  - Não considera vento, temperatura, desnível entre postes nem a tração de projeto do fabricante.
    /// </summary>
    public static class EffortCalculator
    {
        /// <summary>Tração do vão em kgf.</summary>
        public static double SpanTensionKgf(double weightKgKm, double spanM)
        {
            double weightKgfPerM = weightKgKm / 1000.0;
            return weightKgfPerM * spanM / (8.0 * FiberSettings.SagRatio);
        }

        /// <summary>Vetor de tração que o vão de <paramref name="at"/> até <paramref name="toward"/> exerce no ponto "at".</summary>
        public static Vector2d PullVector(Point3d at, Point3d toward, double weightKgKm)
        {
            var v = new Vector2d(toward.X - at.X, toward.Y - at.Y);
            double span = v.Length;
            if (span < 1e-6) return new Vector2d(0, 0);
            return v.GetNormal() * SpanTensionKgf(weightKgKm, span);
        }

        /// <summary>Esforço no vértice <paramref name="index"/> de uma sequência de postes (um único cabo).</summary>
        public static EffortResult AtPathIndex(IList<Point3d> path, int index, double weightKgKm)
        {
            var result = new EffortResult { CableCount = 1 };
            AddVertex(result, path, index, weightKgKm);
            return result;
        }

        /// <summary>Soma ao resultado a tração dos vãos vizinhos ao vértice <paramref name="index"/> do cabo.</summary>
        private static void AddVertex(EffortResult result, IList<Point3d> path, int index, double weightKgKm)
        {
            Point3d at = path[index];
            Point3d? previous = index > 0 ? path[index - 1] : (Point3d?)null;
            Point3d? next = index < path.Count - 1 ? path[index + 1] : (Point3d?)null;

            if (previous != null) result.Resultant += PullVector(at, previous.Value, weightKgKm);
            if (next != null) result.Resultant += PullVector(at, next.Value, weightKgKm);
            result.AddGeometry(at, previous, next);
        }

        /// <summary>
        /// Esforço num poste somando todos os cabos que têm vértice a até <paramref name="tolerance"/> dele.
        /// De cada cabo é usado só o vértice mais próximo, para não contar duas vezes vãos curtos.
        /// </summary>
        public static EffortResult AtPole(IEnumerable<CableRun> runs, Point3d pole, double tolerance)
        {
            var result = new EffortResult();
            foreach (CableRun run in runs)
            {
                int best = -1;
                double bestDist = tolerance;
                for (int v = 0; v < run.Vertices.Count; v++)
                {
                    double d = run.Vertices[v].DistanceTo(pole);
                    if (d <= bestDist) { bestDist = d; best = v; }
                }
                if (best < 0) continue;

                AddVertex(result, run.Vertices, best, run.WeightKgKm);
                result.CableCount++;
                result.Cables.Add($"{run.Name} ({run.WeightKgKm} kg/km)");
            }
            return result;
        }

        /// <summary>
        /// Todos os cabos do espaço informado com peso conhecido. Cabos cujo tipo não está na planilha
        /// são ignorados e listados em <paramref name="unknownCables"/>.
        /// </summary>
        public static List<CableRun> CollectCables(Transaction tr, BlockTableRecord space,
            List<CableModel> catalog, ISet<string>? unknownCables = null)
        {
            var runs = new List<CableRun>();
            foreach (ObjectId id in space)
            {
                CableRun? run = ToCableRun(tr.GetObject(id, OpenMode.ForRead), catalog, unknownCables);
                if (run != null) runs.Add(run);
            }
            return runs;
        }

        /// <summary>
        /// Converte a entidade em CableRun se ela for um cabo do plugin com peso cadastrado.
        /// Cabos fora da planilha vão para <paramref name="unknownCables"/>.
        /// </summary>
        public static CableRun? ToCableRun(DBObject obj, List<CableModel> catalog, ISet<string>? unknownCables = null)
        {
            if (obj is not Polyline poly) return null;

            string? name = XDataTags.GetCableName(poly);
            if (name == null) return null;

            CableModel? model = CableProvider.Find(catalog, name);
            if (model == null)
            {
                unknownCables?.Add(name);
                return null;
            }

            var run = new CableRun { Name = model.ShortName, WeightKgKm = model.WeightKgKm };
            for (int v = 0; v < poly.NumberOfVertices; v++) run.Vertices.Add(poly.GetPoint3dAt(v));
            return run;
        }
    }
}
