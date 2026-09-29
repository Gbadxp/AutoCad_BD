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
    public class CheckCommand
    {
        public const string Layer = "FIBRA_VERIFICACAO";   // Não imprime

        /// <summary>
        /// Confere o projeto com a NDU 009 (veja NormCheck), lista as não conformidades na linha de comando e marca
        /// cada uma no desenho com um círculo vermelho numa layer que não imprime. As marcas da verificação anterior
        /// são apagadas; rodando de novo depois de corrigir, só sobra o que ainda falta.
        /// </summary>
        [CommandMethod("FIBRA_VERIFICAR")]
        public void Check()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<CableModel> catalog = CableProvider.GetCables(ed);
            ProjectData project = ProjectData.Collect(db, catalog);
            List<NormIssue> issues = NormCheck.Run(project);

            double scale = DrawingScale.Factor(db);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);

                // Apaga as marcas anteriores (coleta antes de apagar)
                List<ObjectId> previous = modelSpace.Cast<ObjectId>()
                    .Where(id => tr.GetObject(id, OpenMode.ForRead) is Entity ent && XDataTags.IsCheck(ent))
                    .ToList();
                foreach (ObjectId id in previous) tr.GetObject(id, OpenMode.ForWrite).Erase();

                if (issues.Any(i => i.Point != Point3d.Origin))
                {
                    CadHelpers.EnsureNonPlottingLayer(tr, db, Layer, 1);
                    int n = 1;
                    foreach (NormIssue issue in issues)
                    {
                        if (issue.Point == Point3d.Origin) continue;
                        var circle = CadHelpers.Append(tr, modelSpace, new Circle(issue.Point, Vector3d.ZAxis, 4.0 * scale) { Layer = Layer });
                        MText text = CadHelpers.AddText(tr, modelSpace, issue.Point + new Vector3d(4.5 * scale, 4.5 * scale, 0),
                            $"{n++}. {issue.Message}", 0, AttachmentPoint.BottomLeft, Layer);
                        XDataTags.TagCheck(tr, db, circle);
                        XDataTags.TagCheck(tr, db, text);
                    }
                }
                tr.Commit();
            }

            int errors = issues.Count(i => i.Severity == NormIssue.Error);
            int warnings = issues.Count - errors;
            ed.WriteMessage($"\n--- VERIFICAÇÃO NDU 009: {project.Occupied.Count} poste(s) ocupado(s), {project.Runs.Count} cabo(s) ---");
            int k = 1;
            foreach (NormIssue issue in issues)
            {
                ed.WriteMessage($"\n{(issue.Point != Point3d.Origin ? k++ + "." : "-")} [{issue.Severity}] {issue.Where}: {issue.Message} (NDU 009, {issue.Rule})");
            }

            if (issues.Count == 0) ed.WriteMessage("\n[SUCESSO]: Nenhuma não conformidade encontrada.");
            else ed.WriteMessage($"\n[INFO]: {errors} erro(s) e {warnings} aviso(s). Marcados no desenho na layer {Layer} (não imprime).");
            ed.WriteMessage("\n[INFO]: Não verificados pelo plugin: orientação dos postes DT, drops por vão e afastamentos da rede elétrica.");
            ed.Regen();
        }
    }
}
