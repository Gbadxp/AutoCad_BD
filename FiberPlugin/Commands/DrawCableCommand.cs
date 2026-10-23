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
    public class DrawCableCommand
    {
        [CommandMethod("FIBRA_LANCAR_CABO")]
        public void DrawCable()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            CableModel? cable = CadHelpers.SelectCable(ed, "Lançar");
            if (cable == null) return;

            List<Point3d>? points = CadHelpers.GetPointSequence(ed,
                "\nSelecione o ponto inicial do cabo: ",
                "\nSelecione o próximo ponto (ou pressione Enter para finalizar): ");
            if (points == null) return;

            if (points.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: É necessário pelo menos dois pontos para lançar um cabo.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                Polyline poly = CableDrawing.Draw(tr, db, space, points, CableDrawing.SpanLengths(points), cable);

                ed.WriteMessage($"\n[AVISO]: Cabo '{cable.FullName}' lançado com sucesso!");
                ed.WriteMessage($"\n[COMPRIMENTO TOTAL]: {poly.Length:F2} metros.");

                tr.Commit();
            }
            ed.UpdateScreen();
        }

        /// <summary>
        /// Troca o tipo de cabos já lançados (ex.: 06F.O lançado no lugar de 12F.O): escolhe o cabo novo e clica nos cabos
        /// a trocar, um por vez, até o Enter. O percurso fica; mudam a layer, o nome no XData e os textos do cabo.
        /// </summary>
        [CommandMethod("FIBRA_TROCAR_CABO", CommandFlags.UsePickSet)]
        public void ChangeCable()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            // Cabos já selecionados antes do comando são trocados direto (o resto da seleção não conta)
            PromptSelectionResult implied = ed.SelectImplied();
            var preselected = new List<ObjectId>();
            if (implied.Status == PromptStatus.OK)
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    preselected = implied.Value.GetObjectIds()
                        .Where(id => tr.GetObject(id, OpenMode.ForRead) is Polyline p && XDataTags.GetCableName(p) != null).ToList();
                }
            }

            CableModel? cable = CadHelpers.SelectCable(ed, "Trocar");
            if (cable == null) return;

            var changed = new List<(string OldName, List<Point3d> Vertices)>();
            foreach (ObjectId id in preselected) Change(id);
            if (preselected.Count == 0 || changed.Count == 0)
            {
                while (true)
                {
                    var options = new PromptEntityOptions($"\nClique no cabo a trocar para {cable.ShortName} (ou Enter para terminar): ")
                    {
                        AllowNone = true
                    };
                    options.SetRejectMessage("\nIsso não é uma polilinha de cabo.");
                    options.AddAllowedClass(typeof(Polyline), true);
                    PromptEntityResult picked = ed.GetEntity(options);
                    if (picked.Status != PromptStatus.OK) break;
                    Change(picked.ObjectId);
                }
            }
            if (changed.Count == 0) return;
            if (changed.Count > 1) ed.WriteMessage($"\n[SUCESSO]: {changed.Count} cabos trocados para {cable.ShortName}.");
            WarnAfterChange(ed, db, cable, changed);

            void Change(ObjectId id)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Polyline poly || XDataTags.GetCableName(poly) is not string oldName)
                    {
                        ed.WriteMessage("\n[AVISO]: Isso não é um cabo do plugin (polilinha numa layer FIBRA_CABO_...).");
                        return;
                    }
                    if (oldName.Equals(cable.ShortName, StringComparison.OrdinalIgnoreCase) &&
                        poly.Layer.Equals(CableDrawing.LayerFor(cable), StringComparison.OrdinalIgnoreCase))
                    {
                        ed.WriteMessage($"\n[INFO]: Esse cabo já é {cable.ShortName}.");
                        return;
                    }
                    if (CadHelpers.IsOnLockedLayer(tr, id))
                    {
                        ed.WriteMessage($"\n[AVISO]: A layer {poly.Layer} está travada: destrave para trocar o cabo {oldName}.");
                        return;
                    }

                    BlockTableRecord space = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                    var others = new List<IReadOnlyList<(double X, double Y)>>();
                    foreach (ObjectId other in space)
                    {
                        if (other != id && tr.GetObject(other, OpenMode.ForRead) is Polyline p && XDataTags.GetCableName(p) != null)
                            others.Add(CableDrawing.Path(p));
                    }
                    int texts = CableDrawing.ChangeType(tr, db, space, poly, cable, others);
                    changed.Add((oldName, Enumerable.Range(0, poly.NumberOfVertices).Select(i => poly.GetPoint3dAt(i)).ToList()));
                    ed.WriteMessage($"\n[INFO]: Cabo {oldName} ({poly.Length:F1} m) trocado para {cable.ShortName}, com {texts} texto(s).");
                    tr.Commit();
                }
            }
        }

        /// <summary>
        /// Depois da troca: avisa se a tração do cabo novo é outra (as setas de esforço ficaram desatualizadas) e se algum
        /// vão passa do que o cabo novo aguenta (ASU-80: 80 m).
        /// </summary>
        private static void WarnAfterChange(Editor ed, Database db, CableModel cable, List<(string OldName, List<Point3d> Vertices)> changed)
        {
            List<CableModel> catalog = CableProvider.GetCables();
            Traction traction = Traction.Load(db);
            const double sampleSpan = 40;
            bool tractionChanged = changed.Select(c => c.OldName).Distinct(StringComparer.OrdinalIgnoreCase).Any(name =>
                CableProvider.Find(catalog, name) is not CableModel old ||
                Math.Abs(traction.Tension(old, sampleSpan) - traction.Tension(cable, sampleSpan)) > 0.001);
            if (tractionChanged)
            {
                ed.WriteMessage($"\n[AVISO]: A tração do {cable.ShortName} é diferente da do cabo de antes: rode o Esforço no Percurso " +
                                "de novo para atualizar as setas de esforço.");
            }

            if (cable.RatedSpanM is double rated)
            {
                double longest = changed.SelectMany(c => CableDrawing.SpanLengths(c.Vertices)).DefaultIfEmpty(0).Max();
                if (longest > rated + 0.5)
                {
                    ed.WriteMessage($"\n[AVISO]: Há vão de {longest:F1} m, acima dos {rated:F0} m do {cable.ShortName} (vão do fabricante).");
                }
            }
        }
    }
}
