using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Esforço num ponto do projeto: calculado dos cabos do desenho em cada poste ocupado (e onde um cabo termina ou faz
    /// ângulo sem poste), ou lido de uma seta sem cabo desenhado (Esforço de 1 Cabo). Já comparado com o nominal.
    /// </summary>
    public sealed class EffortPoint
    {
        public Point3d Point { get; set; }             // De onde sai a seta (centro do poste, ou o ponto do cabo sem poste)
        public PoleInfo? Pole { get; set; }
        public double Kgf { get; set; }                // Resultante dos cabos na altura de fixação
        public double AngleDeg { get; set; }           // Direção da resultante (anti-horário a partir do leste)
        public string Situation { get; set; } = "";    // Fim de rede, Ângulo (x°), Passagem
        public int CableCount { get; set; }

        /// <summary>A NDU 009 pede a seta aqui: fim de rede ou deflexão acima de 10°, com resultante não nula.</summary>
        public bool NeedsArrow { get; set; }

        /// <summary>A seta está no desenho.</summary>
        public bool HasArrow { get; set; }

        /// <summary>A seta do desenho mostra outro valor (cabo mudou depois dela, ou foi feita no modo Cabo).</summary>
        public bool ArrowOutdated { get; set; }

        public PoleLoad Load { get; set; } = null!;

        public static EffortPoint Calculated(Point3d point, PoleInfo? pole, EffortResult result, double attachHeight) => new EffortPoint
        {
            Point = point,
            Pole = pole,
            Kgf = result.Kgf,
            AngleDeg = result.AngleDeg,
            Situation = result.Situation,
            CableCount = result.CableCount,
            NeedsArrow = result.NeedsArrow,
            Load = PoleLoad.For(pole, result.Kgf, attachHeight)
        };

        public static EffortPoint FromMarker(EffortMarkerData marker, PoleInfo? pole, double attachHeight) => new EffortPoint
        {
            Point = marker.Point,
            Pole = pole,
            Kgf = marker.Kgf,
            AngleDeg = marker.AngleDeg,
            Situation = marker.Situation,
            CableCount = marker.CableCount,
            NeedsArrow = true,
            HasArrow = true,
            Load = PoleLoad.For(pole, marker.Kgf, attachHeight)
        };
    }

    /// <summary>Tudo o que o relatório e o memorial levantam do desenho (Model), com as mesmas regras nos dois.</summary>
    public sealed class ProjectData
    {
        /// <summary>Diferença (kgf) a partir da qual a seta é considerada desatualizada em relação ao cálculo.</summary>
        private const double OutdatedKgf = 0.05;

        public List<PoleInfo> PoleList { get; private set; } = new List<PoleInfo>();   // Em ordem de número
        public List<BoxInfo> BoxList { get; private set; } = new List<BoxInfo>();      // CTO e depois CEO
        public List<CableTotal> CableTotals { get; private set; } = new List<CableTotal>();
        public List<CableRun> Runs { get; private set; } = new List<CableRun>();

        /// <summary>Postes com cabo do projeto (um ponto de fixação cada) e a situação de cada um.</summary>
        public List<(PoleInfo Pole, EffortResult Result)> Occupied { get; private set; } = new List<(PoleInfo, EffortResult)>();

        /// <summary>
        /// Esforço de todos os postes ocupados, com ou sem seta (NDU 009, item 13: esforço em cada poste), mais os pontos
        /// sem poste que pedem seta e as setas sem cabo desenhado. Em ordem de poste (pontos sem poste no fim).
        /// </summary>
        public List<EffortPoint> Efforts { get; private set; } = new List<EffortPoint>();

        /// <summary>Setas em pontos que a norma dispensa (passagem até 10° ou resultante nula): sobraram de cálculo anterior.</summary>
        public List<Point3d> ExtraArrows { get; private set; } = new List<Point3d>();

        /// <summary>Blocos de equipamentos elétricos da Energisa (trafo, chaves, para-raios...), com o nome do bloco.</summary>
        public List<(string Name, Point3d Position)> Equipment { get; private set; } = new List<(string, Point3d)>();

        /// <summary>Setas de versões antigas, sem os dados do cálculo.</summary>
        public int LegacyMarkers { get; private set; }

        public Traction Traction { get; private set; } = null!;

        public bool IsEmpty => PoleList.Count == 0 && BoxList.Count == 0 && Efforts.Count == 0 && CableTotals.Count == 0;

        public static ProjectData Collect(Database db, List<CableModel> catalog)
        {
            var data = new ProjectData { Traction = Traction.Load(db) };
            double attachHeight = data.Traction.Settings.AttachHeightM;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);

                data.PoleList = Poles.Collect(tr, modelSpace)
                    .OrderBy(p => Poles.ParseNumber(p.Number) ?? int.MaxValue)
                    .ThenBy(p => p.Number)
                    .ToList();
                data.BoxList = Boxes.Collect(tr, modelSpace)
                    .OrderBy(b => b.Data.Kind == BlockCategories.Cto ? 0 : 1)
                    .ThenBy(b => b.Data.Number)
                    .ToList();
                data.CableTotals = CableDrawing.Totals(tr, modelSpace, catalog);
                data.Runs = EffortCalculator.CollectCables(tr, modelSpace, catalog, data.Traction);

                // Esforço calculado dos cabos, com os mesmos pontos do Esforço no Percurso (modo Total)
                var calculatedPoints = new HashSet<string>();
                foreach (EffortStop stop in EffortCalculator.Stops(data.Runs, data.PoleList))
                {
                    EffortResult result = EffortCalculator.AtPole(data.Runs, stop.Point, stop.Tolerance);
                    if (result.CableCount == 0) continue;

                    calculatedPoints.Add(Key(stop.Point));
                    if (stop.Pole != null) data.Occupied.Add((stop.Pole, result));
                    else if (!result.NeedsArrow) continue; // Vértice sem poste em linha reta: só desenho do cabo
                    data.Efforts.Add(EffortPoint.Calculated(stop.Point, stop.Pole, result, attachHeight));
                }
                data.Occupied = data.Occupied.OrderBy(o => data.PoleList.IndexOf(o.Pole)).ToList();

                var polesByHandle = data.PoleList.ToDictionary(p => p.Id.Handle.ToString(), StringComparer.OrdinalIgnoreCase);
                var markers = new List<(EffortMarkerData Marker, PoleInfo? Pole)>();
                var seenPoints = new HashSet<string>();
                var legacyPoints = new HashSet<string>();
                foreach (ObjectId id in modelSpace)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                    if (ent is BlockReference br && CadHelpers.GetBlockName(tr, br) is string blockName &&
                        BlockCategories.Of(blockName) == BlockCategories.Electrical)
                    {
                        data.Equipment.Add((blockName, br.Position));
                        continue;
                    }

                    // A seta e os textos de um mesmo ponto guardam os mesmos dados
                    EffortMarkerData? marker = XDataTags.ReadEffortMarker(ent);
                    if (marker != null)
                    {
                        if (!seenPoints.Add(Key(marker.Point))) continue;

                        polesByHandle.TryGetValue(marker.PoleHandle, out PoleInfo? pole);
                        pole ??= Poles.Nearest(data.PoleList, marker.Point, 0.01);
                        markers.Add((marker, pole));
                    }
                    else if (XDataTags.TryGetEffortPole(ent, out Point3d oldPoint) && legacyPoints.Add(Key(oldPoint)))
                    {
                        data.LegacyMarkers++;
                    }
                }
                tr.Commit();

                // Setas do desenho: confirmam o ponto calculado, sobram onde a norma dispensa, ou são cálculo sem cabo desenhado
                foreach (var (marker, pole) in markers)
                {
                    EffortPoint? calculated = pole != null
                        ? data.Efforts.FirstOrDefault(e => e.Pole == pole)
                        : data.Efforts.FirstOrDefault(e => e.Pole == null && Key(e.Point) == Key(marker.Point));

                    if (calculated != null && calculated.NeedsArrow)
                    {
                        calculated.HasArrow = true;
                        if (Math.Abs(calculated.Kgf - marker.Kgf) > OutdatedKgf) calculated.ArrowOutdated = true;
                    }
                    else if (calculated != null || calculatedPoints.Contains(Key(marker.Point)))
                    {
                        data.ExtraArrows.Add(marker.Point);
                    }
                    else
                    {
                        data.Efforts.Add(EffortPoint.FromMarker(marker, pole, attachHeight));
                    }
                }
            }

            data.Efforts = data.Efforts
                .OrderBy(e => e.Pole == null)
                .ThenBy(e => e.Pole != null ? Poles.ParseNumber(e.Pole.Number) ?? int.MaxValue : int.MaxValue)
                .ToList();
            return data;
        }

        /// <summary>Maior vão do cabo no projeto, em metros (0 se ele não tem vãos).</summary>
        public double MaxSpan(string cableName) => Runs
            .Where(r => r.Name.Equals(cableName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Vertices.Zip(r.Vertices.Skip(1), (a, b) => a.DistanceTo(b)))
            .DefaultIfEmpty(0)
            .Max();

        private static string Key(Point3d p) =>
            Math.Round(p.X, 2).ToString(CultureInfo.InvariantCulture) + ";" + Math.Round(p.Y, 2).ToString(CultureInfo.InvariantCulture);
    }
}
