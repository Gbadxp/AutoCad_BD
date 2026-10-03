using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class AutoRouteCommand
    {
        private static readonly string Sides = string.Join(" ", RouteSides.All);

        // Último lado escolhido, lembrado enquanto o AutoCAD estiver aberto. Não vai para o desenho: a entrada
        // FIBRA_PLUGIN_ROTA gravada pelas versões 1.9.30-1.9.33 ficou quebrada num desenho recuperado de um crash, e
        // abrir essa entrada derruba o AutoCAD. Não volte a ler essa chave.
        private static string lastSide = RouteSides.Up;

        /// <summary>
        /// Rota mais curta entre os blocos selecionados, afastada 1,8 m dos postes do lado escolhido (cima, baixo,
        /// esquerda ou direita), sempre do mesmo lado da rota. Nos postes DT o cabo vai na face do poste.
        /// </summary>
        [CommandMethod("FIBRA_ROTEAMENTO_AUTO")]
        public void AutoRouteCable()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            CableModel? cable = CadHelpers.SelectCable(ed, "Lançar");
            if (cable == null) return;

            // De que lado dos postes o cabo passa (o último escolhido é o padrão). Perguntado antes da seleção, que é
            // lida logo depois de selecionar.
            string? side = CadHelpers.AskKeyword(ed, $"\nLado do cabo em relação aos postes [Cima/Baixo/Esquerda/Direita] <{lastSide}>: ", Sides, lastSide);
            if (side == null) return;
            lastSide = side;

            // Solicitar seleção de blocos (postes, caixas, etc.)
            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione os elementos (blocos) para o roteamento automático: "
            };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK || psr.Value.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: Selecione pelo menos 2 elementos para criar um roteamento.");
                return;
            }
            ObjectId[] selected = psr.Value.GetObjectIds();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var positions = new List<Point3d>();
                var faces = new List<(Point3d At, Vector2d Face)>(); // Postes DT: eixo das faces
                foreach (ObjectId id in selected)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br) continue;
                    positions.Add(br.Position);
                    if (DoubleTFace(tr, br) is Vector2d face) faces.Add((br.Position, face));
                }

                // Blocos no mesmo ponto (ex.: CTO sobre o poste) viram um único ponto de passagem
                List<Point3d> points = RouteOptimizer.Dedupe(positions, 0.01);
                if (points.Count < 2)
                {
                    ed.WriteMessage("\n[AVISO]: Os elementos selecionados estão todos no mesmo ponto.");
                    tr.Commit();
                    return;
                }

                List<Point3d> ordered = RouteOptimizer.Order(points);
                List<Vector2d?> orderedFaces = ordered
                    .Select(p => faces.Where(f => f.At.DistanceTo(p) <= 0.01).Select(f => (Vector2d?)f.Face).FirstOrDefault())
                    .ToList();

                // Afasta o cabo do centro do poste, do lado escolhido (na face dos DT), sem que os cantos se afastem
                // mais que o previsto
                var sideways = new List<int>();
                List<Point3d> vertices = RouteOptimizer.OffsetPath(ordered, FiberSettings.AutoRouteOffset,
                    side, orderedFaces, sideways);

                // A metragem escrita é o vão real entre postes
                List<double> spans = CableDrawing.SpanLengths(ordered);

                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                CableDrawing.Draw(tr, db, modelSpace, vertices, spans, cable);

                int dtCount = orderedFaces.Count(f => f != null) - sideways.Count;
                ed.WriteMessage($"\n[SUCESSO]: Roteamento automático do cabo '{cable.FullName}' concluído! " +
                                $"{ordered.Count} pontos conectados, cabo do lado de {side.ToLowerInvariant()}" +
                                (dtCount > 0 ? $" ({dtCount} poste(s) DT com o cabo na face)" : "") +
                                $". Metragem entre postes: {spans.Sum():F2} m");
                if (sideways.Count > 0)
                {
                    ed.WriteMessage($"\n[AVISO]: {sideways.Count} poste(s) DT estão com as faces na direção do cabo " +
                                    "(bloco girado de lado); neles o cabo foi afastado como num poste redondo. " +
                                    "Gire o bloco do DT e rode o roteamento de novo se quiser o cabo na face.");
                }

                tr.Commit();
            }
            ed.UpdateScreen();
        }

        /// <summary>
        /// Eixo das faces de um poste DT (o eixo Y do bloco: no BLOCOS.dwg as abas do "I" ficam em cima e embaixo),
        /// já com a rotação do bloco. Null para poste redondo e para os outros blocos (CTO, CEO...).
        /// </summary>
        private static Vector2d? DoubleTFace(Transaction tr, BlockReference br)
        {
            string name = CadHelpers.GetBlockName(tr, br);
            bool doubleT = XDataTags.ReadPole(br) is PoleData data
                ? data.Type == PoleData.DoubleT
                : BlockCategories.Of(name) == BlockCategories.Poles && BlockCategories.Words(name).Contains(PoleData.DoubleT);
            return doubleT ? new Vector2d(-Math.Sin(br.Rotation), Math.Cos(br.Rotation)) : (Vector2d?)null;
        }
    }
}
