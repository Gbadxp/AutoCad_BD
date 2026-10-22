using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using GI = Autodesk.AutoCAD.GraphicsInterface;

namespace FiberPlugin.Commands
{
    /// <summary>Reserva técnica de cabo e cordoalha, desenhadas como no modelo de projeto.</summary>
    public class CableExtrasCommands
    {
        /// <summary>Última metragem de reserva usada (vale para a próxima vez, até fechar o AutoCAD).</summary>
        private static double _lastReserveMeters = 30;

        /// <summary>
        /// Pede a metragem e, para cada reserva, o ponto (poste ou cabo) e o lado do texto, mostrando a seta enquanto o
        /// mouse anda. Repete até o Enter.
        /// </summary>
        [CommandMethod("FIBRA_RESERVA_TECNICA")]
        public void PlaceReserve()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            var metersOptions = new PromptDoubleOptions("\nComprimento da reserva técnica (m)")
            {
                DefaultValue = _lastReserveMeters,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult metersResult = ed.GetDouble(metersOptions);
            if (metersResult.Status != PromptStatus.OK) return;
            double meters = metersResult.Value;
            _lastReserveMeters = meters;

            List<PoleInfo> poles;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadHelpers.EnsureLayer(tr, db, TechnicalReserve.Layer, TechnicalReserve.Color); // A prévia já usa a layer
                poles = Poles.Collect(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                tr.Commit();
            }

            int placed = 0;
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var pointOptions = new PromptPointOptions(placed == 0
                    ? "\nClique no ponto da reserva (poste ou cabo): "
                    : "\nClique no ponto da próxima reserva (ou Enter para terminar): ") { AllowNone = true };
                PromptPointResult pointResult = ed.GetPoint(pointOptions);
                if (pointResult.Status != PromptStatus.OK) break;

                // Clicou no poste: a seta aponta para o centro dele, com a ponta encostada no desenho do poste
                Point3d clicked = pointResult.Value.TransformBy(ucs);
                PoleInfo? pole = Poles.Nearest(poles, clicked, FiberSettings.PoleMatchTolerance);
                Point3d anchor = pole?.Position ?? clicked;

                var jig = new ReserveJig(db, anchor, pole != null, meters);
                PromptResult side = ed.Drag(jig);
                jig.Clear();
                if (side.Status != PromptStatus.OK) break;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                    foreach (Entity ent in TechnicalReserve.Build(db, anchor, pole != null, jig.Cursor, meters))
                    {
                        CadHelpers.Append(tr, space, ent);
                        XDataTags.TagReserve(tr, db, ent, meters);
                    }
                    tr.Commit();
                }
                placed++;
                ed.WriteMessage($"\n[INFO]: {TechnicalReserve.Text(meters)}" + (pole != null ? $" no poste {pole.Number}." : "."));
            }
            if (placed > 1) ed.WriteMessage($"\n[SUCESSO]: {placed} reservas técnicas de {meters:0.#} m lançadas.");
        }

        /// <summary>Linha da cordoalha clicando nos postes, como no Lançar Cabo, com a metragem de cada trecho.</summary>
        [CommandMethod("FIBRA_CORDOALHA")]
        public void DrawStrand()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            List<Point3d>? points = CadHelpers.GetPointSequence(ed,
                "\nSelecione o ponto inicial da cordoalha: ",
                "\nSelecione o próximo ponto (ou pressione Enter para finalizar): ", previewColor: 30);
            if (points == null) return;
            if (points.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: É necessário pelo menos dois pontos para lançar a cordoalha.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline poly = Strand.Draw(tr, db, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite), points);
                tr.Commit();
                ed.WriteMessage($"\n[SUCESSO]: Cordoalha lançada: {poly.Length:F2} m em {points.Count - 1} trecho(s).");
            }
            ed.UpdateScreen();
        }

        /// <summary>Prévia da reserva: a seta e o texto acompanham o mouse até o clique.</summary>
        private sealed class ReserveJig : DrawJig
        {
            private readonly Database _db;
            private readonly Point3d _anchor;
            private readonly bool _atPole;
            private readonly double _meters;
            private List<Entity> _shown = new List<Entity>();

            public ReserveJig(Database db, Point3d anchor, bool atPole, double meters)
            {
                _db = db;
                _anchor = anchor;
                _atPole = atPole;
                _meters = meters;
                Cursor = anchor;
            }

            /// <summary>Ponto do mouse (lado e comprimento da seta), em coordenadas do mundo.</summary>
            public Point3d Cursor { get; private set; }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\nClique para o lado em que vai o texto da reserva: ")
                {
                    UseBasePoint = true,
                    BasePoint = _anchor,
                    UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.GovernedByOrthoMode
                };
                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                if (result.Value.DistanceTo(Cursor) < 1e-9) return SamplerStatus.NoChange;
                Cursor = result.Value;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(GI.WorldDraw draw)
            {
                Clear();
                if (Cursor.DistanceTo(_anchor) < 1e-9) return true;
                _shown = TechnicalReserve.Build(_db, _anchor, _atPole, Cursor, _meters);
                foreach (Entity ent in _shown) draw.Geometry.Draw(ent);
                return true;
            }

            public void Clear()
            {
                foreach (Entity ent in _shown) ent.Dispose();
                _shown.Clear();
            }
        }
    }
}
