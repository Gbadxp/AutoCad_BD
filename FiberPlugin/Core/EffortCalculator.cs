using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>Um cabo desenhado, com o modelo do catálogo e a regra de tração do projeto.</summary>
    public class CableRun
    {
        public CableModel Model { get; set; } = new CableModel();
        public List<Point3d> Vertices { get; set; } = new List<Point3d>();

        /// <summary>Tração (kgf) de um vão deste cabo com o comprimento informado (m).</summary>
        public Func<double, double> Tension { get; set; } = _ => 0;

        public string Name => Model.ShortName;
    }

    public class EffortResult
    {
        public Vector2d Resultant { get; set; } = new Vector2d(0, 0);
        public int CableCount { get; set; }
        public List<string> Cables { get; } = new List<string>();

        /// <summary>Esforço resultante em kgf.</summary>
        public double Kgf => Resultant.Length;

        /// <summary>Resultante até este valor (kgf) é tratada como nula: vãos alinhados que se anulam.</summary>
        public const double NullKgf = 0.1;

        /// <summary>Ângulo da resultante em radianos (0 a 2π). Zero quando o esforço é desprezível.</summary>
        public double AngleRad => Kgf > NullKgf ? Resultant.Angle : 0;
        public double AngleDeg => AngleRad * 180.0 / Math.PI;

        /// <summary>Cabos que terminam no poste (ancoragem) e que passam por ele.</summary>
        public int EndCount { get; set; }
        public int PassCount { get; set; }

        /// <summary>Maior deflexão (mudança de direção) entre os vãos de um cabo que passa pelo poste, em graus.</summary>
        public double MaxDeflectionDeg { get; set; }

        /// <summary>Deflexão a partir da qual o poste é considerado "de ângulo".</summary>
        public const double AngleThresholdDeg = 5;

        /// <summary>Deflexão acima da qual a seta é obrigatória com cabo óptico (NDU 009, Anexo B, 2.2.18).</summary>
        public const double ArrowDeflectionDeg = 10;

        /// <summary>Situação do poste no percurso: "Fim de rede", "Ângulo (35°)" ou "Passagem".</summary>
        public string Situation =>
            EndCount > 0 ? "Fim de rede"
            : MaxDeflectionDeg >= AngleThresholdDeg ? $"Ângulo ({MaxDeflectionDeg:F0}°)"
            : "Passagem";

        /// <summary>
        /// O ponto leva a seta de esforço. A NDU 009 (Anexo B, 2.2.18) só a exige no poste de fim de rede (aqui também o
        /// poste onde um dos cabos termina, que é ancoragem) e no de deflexão acima de 10°, e a dispensa onde o esforço
        /// resultante é nulo (item 16.3 h). Nos postes de passagem o esforço continua calculado (memorial e relatório).
        /// </summary>
        public bool NeedsArrow => Kgf > NullKgf && (EndCount > 0 || MaxDeflectionDeg > ArrowDeflectionDeg);

        /// <summary>Por que o ponto fica sem seta, para as mensagens dos comandos (null quando ele leva seta).</summary>
        public string? WithoutArrowReason =>
            NeedsArrow ? null
            : Kgf <= NullKgf ? "resultante nula, sem seta"
            : $"deflexão de {MaxDeflectionDeg:F0}°, sem seta (só acima de {ArrowDeflectionDeg:F0}° ou em fim de rede)";

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

    /// <summary>Ponto de esforço do percurso: o poste a que os vértices dos cabos foram ligados, ou o vértice sem poste perto.</summary>
    public sealed class EffortStop
    {
        public Point3d Point { get; set; }
        public PoleInfo? Pole { get; set; }

        /// <summary>Raio de busca dos vértices dos cabos em volta do ponto (alcança o vértice que levou até este poste).</summary>
        public double Tolerance { get; set; }
    }

    /// <summary>
    /// Esforço resultante no poste pela soma vetorial das trações dos vãos que chegam nele (método
    /// analítico da NDU 009, Anexo A). A tração de cada vão vem da Tabela 08 da norma ou do peso do cabo
    /// com flecha de 1% (veja Traction). Não considera vento, temperatura nem desnível entre postes.
    /// </summary>
    public static class EffortCalculator
    {
        /// <summary>
        /// Pontos de esforço dos cabos: cada vértice vai para o poste mais próximo a até PoleLinkRadius, e o esforço é
        /// calculado a partir do centro desse poste; sem poste por perto, o ponto é o próprio vértice. Usado pelo
        /// Esforço no Percurso e pelo levantamento do relatório e do memorial, para os dois darem o mesmo resultado.
        /// </summary>
        public static List<EffortStop> Stops(IEnumerable<CableRun> runs, IList<PoleInfo> poles)
        {
            var stops = new List<EffortStop>();
            foreach (CableRun run in runs)
            {
                foreach (Point3d vertex in run.Vertices)
                {
                    PoleInfo? pole = Poles.Nearest(poles, vertex, FiberSettings.PoleLinkRadius);
                    Point3d point = pole?.Position ?? vertex;
                    double tolerance = Math.Max(FiberSettings.PoleMatchTolerance, vertex.DistanceTo(point) + 0.1);

                    EffortStop? existing = stops.Find(s => s.Point.DistanceTo(point) < 0.01);
                    if (existing != null) existing.Tolerance = Math.Max(existing.Tolerance, tolerance);
                    else stops.Add(new EffortStop { Point = point, Pole = pole, Tolerance = tolerance });
                }
            }
            return stops;
        }

        /// <summary>Vetor de tração que o vão de <paramref name="at"/> até <paramref name="toward"/> exerce no ponto "at".</summary>
        public static Vector2d PullVector(Point3d at, Point3d toward, Func<double, double> tension)
        {
            var v = new Vector2d(toward.X - at.X, toward.Y - at.Y);
            double span = v.Length;
            if (span < 1e-6) return new Vector2d(0, 0);
            return v.GetNormal() * tension(span);
        }

        /// <summary>Esforço no vértice <paramref name="index"/> de uma sequência de postes (um único cabo).</summary>
        public static EffortResult AtPathIndex(IList<Point3d> path, int index, Func<double, double> tension)
        {
            var result = new EffortResult { CableCount = 1 };
            AddVertex(result, path, index, tension);
            return result;
        }

        /// <summary>Soma ao resultado a tração dos vãos vizinhos ao vértice <paramref name="index"/> do cabo.</summary>
        private static void AddVertex(EffortResult result, IList<Point3d> path, int index, Func<double, double> tension)
        {
            Point3d at = path[index];
            Point3d? previous = index > 0 ? path[index - 1] : (Point3d?)null;
            Point3d? next = index < path.Count - 1 ? path[index + 1] : (Point3d?)null;

            if (previous != null) result.Resultant += PullVector(at, previous.Value, tension);
            if (next != null) result.Resultant += PullVector(at, next.Value, tension);
            result.AddGeometry(at, previous, next);
        }

        /// <summary>
        /// Esforço num ponto do percurso (veja Stops): soma os cabos que têm vértice ligado a ele, ou seja, cujo poste mais
        /// próximo é o poste do ponto. O raio do ponto pode chegar a 10 m (vértice afastado do poste); sem essa conferência,
        /// o vértice de outro cabo no poste vizinho entraria aqui também e seria contado duas vezes.
        /// </summary>
        public static EffortResult AtStop(IEnumerable<CableRun> runs, EffortStop stop, IList<PoleInfo> poles) =>
            AtPole(runs, stop.Point, stop.Tolerance, v => Poles.Nearest(poles, v, FiberSettings.PoleLinkRadius) == stop.Pole);

        /// <summary>
        /// Esforço num poste somando todos os cabos que têm vértice a até <paramref name="tolerance"/> dele.
        /// De cada cabo é usado só o vértice mais próximo, para não contar duas vezes vãos curtos.
        /// </summary>
        /// <param name="belongs">Filtro dos vértices que contam para este poste (null = todos no raio).</param>
        public static EffortResult AtPole(IEnumerable<CableRun> runs, Point3d pole, double tolerance, Func<Point3d, bool>? belongs = null)
        {
            var result = new EffortResult();
            foreach (CableRun run in runs)
            {
                int best = -1;
                double bestDist = tolerance;
                for (int v = 0; v < run.Vertices.Count; v++)
                {
                    double d = run.Vertices[v].DistanceTo(pole);
                    if (d <= bestDist && (belongs == null || belongs(run.Vertices[v]))) { bestDist = d; best = v; }
                }
                if (best < 0) continue;

                AddVertex(result, run.Vertices, best, run.Tension);
                result.CableCount++;
                result.Cables.Add(run.Name);
            }
            return result;
        }

        /// <summary>
        /// Todos os cabos do espaço informado com peso conhecido. Cabos cujo tipo não está na planilha
        /// são ignorados e listados em <paramref name="unknownCables"/>.
        /// </summary>
        public static List<CableRun> CollectCables(Transaction tr, BlockTableRecord space,
            List<CableModel> catalog, Traction traction, ISet<string>? unknownCables = null)
        {
            var runs = new List<CableRun>();
            foreach (ObjectId id in space)
            {
                CableRun? run = ToCableRun(tr.GetObject(id, OpenMode.ForRead), catalog, traction, unknownCables);
                if (run != null) runs.Add(run);
            }
            return runs;
        }

        /// <summary>
        /// Converte a entidade em CableRun se ela for um cabo do plugin com peso cadastrado.
        /// Cabos fora da planilha vão para <paramref name="unknownCables"/>.
        /// </summary>
        public static CableRun? ToCableRun(DBObject obj, List<CableModel> catalog, Traction traction, ISet<string>? unknownCables = null)
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

            var run = new CableRun { Model = model, Tension = span => traction.Tension(model, span) };
            for (int v = 0; v < poly.NumberOfVertices; v++) run.Vertices.Add(poly.GetPoint3dAt(v));
            return run;
        }
    }
}
