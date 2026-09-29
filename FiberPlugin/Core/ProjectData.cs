using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>Um ponto de esforço do desenho: dados da seta, poste de onde sai o cálculo e comparação com o nominal.</summary>
    public sealed class EffortPoint
    {
        public EffortMarkerData Marker { get; set; } = new EffortMarkerData();
        public PoleInfo? Pole { get; set; }
        public PoleLoad Load { get; set; } = null!;
    }

    /// <summary>Tudo o que o relatório e o memorial levantam do desenho (Model), com as mesmas regras nos dois.</summary>
    public sealed class ProjectData
    {
        public List<PoleInfo> PoleList { get; private set; } = new List<PoleInfo>();   // Em ordem de número
        public List<BoxInfo> BoxList { get; private set; } = new List<BoxInfo>();      // CTO e depois CEO
        public List<CableTotal> CableTotals { get; private set; } = new List<CableTotal>();
        public List<CableRun> Runs { get; private set; } = new List<CableRun>();

        /// <summary>Postes com cabo do projeto (um ponto de fixação cada) e a situação de cada um.</summary>
        public List<(PoleInfo Pole, EffortResult Result)> Occupied { get; private set; } = new List<(PoleInfo, EffortResult)>();

        /// <summary>Pontos de esforço das setas, em ordem de poste (pontos sem poste no fim).</summary>
        public List<EffortPoint> Efforts { get; private set; } = new List<EffortPoint>();

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
                data.Occupied = data.PoleList
                    .Select(p => (Pole: p, Result: EffortCalculator.AtPole(data.Runs, p.Position, FiberSettings.PoleMatchTolerance)))
                    .Where(x => x.Result.CableCount > 0)
                    .ToList();

                var polesByHandle = data.PoleList.ToDictionary(p => p.Id.Handle.ToString(), StringComparer.OrdinalIgnoreCase);
                var seenPoints = new HashSet<string>();
                var legacyPoints = new HashSet<string>();
                foreach (ObjectId id in modelSpace)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                    // A seta e os textos de um mesmo ponto guardam os mesmos dados
                    EffortMarkerData? marker = XDataTags.ReadEffortMarker(ent);
                    if (marker != null)
                    {
                        if (!seenPoints.Add(Key(marker.Point))) continue;

                        polesByHandle.TryGetValue(marker.PoleHandle, out PoleInfo? pole);
                        pole ??= Poles.Nearest(data.PoleList, marker.Point, 0.01);
                        data.Efforts.Add(new EffortPoint { Marker = marker, Pole = pole, Load = PoleLoad.For(pole, marker.Kgf, attachHeight) });
                    }
                    else if (XDataTags.TryGetEffortPole(ent, out Point3d oldPoint) && legacyPoints.Add(Key(oldPoint)))
                    {
                        data.LegacyMarkers++;
                    }
                }
                tr.Commit();
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
