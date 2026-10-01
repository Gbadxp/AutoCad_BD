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
                    Mark(tr, db, modelSpace, issues, scale);
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

        /// <summary>
        /// Um círculo em cada lugar com problema e, ao lado, os problemas dali um embaixo do outro, com o mesmo
        /// número da lista da linha de comando. Se o texto encostar no de outro lugar próximo, ele desce até ficar livre.
        /// </summary>
        private static void Mark(Transaction tr, Database db, BlockTableRecord space, List<NormIssue> issues, double scale)
        {
            double height = DrawingScale.TextHeight(db);
            double lineStep = height * 1.7;   // Espaçamento entre linhas do MText (cerca de 1,67 × a altura)
            double charWidth = height * 0.75; // Largura média de uma letra, para estimar o tamanho do texto

            // Numerados na ordem da linha de comando; os do mesmo lugar (mesmo poste) ficam juntos
            var groups = new List<(Point3d Point, List<string> Lines)>();
            int n = 1;
            foreach (NormIssue issue in issues.Where(i => i.Point != Point3d.Origin))
            {
                string line = $"{n++}. {issue.Message}";
                int index = groups.FindIndex(g => g.Point.DistanceTo(issue.Point) < 0.5);
                if (index >= 0) groups[index].Lines.Add(line);
                else groups.Add((issue.Point, new List<string> { line }));
            }

            var taken = new List<Extents3d>();
            foreach (var (point, lines) in groups)
            {
                var circle = CadHelpers.Append(tr, space, new Circle(point, Vector3d.ZAxis, 4.0 * scale) { Layer = Layer });
                XDataTags.TagCheck(tr, db, circle);

                // Canto de cima à esquerda do texto, junto ao círculo; o texto cresce para baixo
                double left = point.X + 4.5 * scale, top = point.Y + 4.5 * scale;
                double width = lines.Max(l => l.Length) * charWidth, blockHeight = lines.Count * lineStep;
                for (bool moved = true; moved;)
                {
                    moved = false;
                    foreach (Extents3d other in taken)
                    {
                        bool overlaps = left < other.MaxPoint.X && left + width > other.MinPoint.X &&
                                        top > other.MinPoint.Y && top - blockHeight < other.MaxPoint.Y;
                        if (!overlaps) continue;
                        top = other.MinPoint.Y - height * 0.5;
                        moved = true;
                    }
                }
                taken.Add(new Extents3d(new Point3d(left, top - blockHeight, 0), new Point3d(left + width, top, 0)));

                MText text = CadHelpers.AddText(tr, space, new Point3d(left, top, point.Z),
                    string.Join("\\P", lines.Select(CadHelpers.MTextLiteral)), 0, AttachmentPoint.TopLeft, Layer);
                XDataTags.TagCheck(tr, db, text);
            }
        }
    }
}
