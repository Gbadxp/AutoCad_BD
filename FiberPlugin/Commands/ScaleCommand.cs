using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class ScaleCommand
    {
        /// <summary>
        /// Define a escala do desenho. Textos dos vãos, pontos e setas de esforço passam a ser criados no
        /// tamanho certo para essa escala, e as anotações já desenhadas podem ser ajustadas na hora.
        /// </summary>
        [CommandMethod("FIBRA_ESCALA")]
        public void SetScale()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int current = DrawingScale.Get(db);

            int? asked = DrawingScale.Ask(ed, "\nEscala do desenho 1:X (ex.: 500, 1000, 2000)", current);
            if (asked == null) return;
            int scale = asked.Value;

            if (scale == current)
            {
                ed.WriteMessage($"\n[INFO]: Escala mantida em 1:{current}.");
                return;
            }
            ElementScales elements = DrawingScale.GetElements(db);
            Apply(ed, db, current, elements, scale, elements);
        }

        /// <summary>
        /// Grava a escala do desenho e as escalas por elemento novas e pergunta se ajusta o que já está desenhado: cada
        /// tipo de elemento (bloco e texto dos postes, símbolo e texto das CTO/CEO, textos dos cabos e setas) muda na
        /// proporção da escala dele. Usado também pela aba Projeto da janela Configurações.
        /// </summary>
        internal static void Apply(Editor ed, Database db, int scaleBefore, ElementScales elementsBefore, int scale, ElementScales elements)
        {
            // Quanto cada tipo de elemento muda de tamanho
            var ratios = ElementScales.Items.ToDictionary(i => i,
                i => (double)elements.Effective(i, scale) / elementsBefore.Effective(i, scaleBefore));

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DrawingScale.Set(tr, db, scale);
                DrawingScale.SetElements(tr, db, elements);

                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                Annotations existing = FindAnnotations(tr, modelSpace);
                int count = existing.CountChanging(ratios);

                int adjusted = 0, locked = 0;
                if (count > 0 && CadHelpers.AskYes(ed, $"\nAjustar os {count} elementos já desenhados para as escalas novas? [Sim/Nao] <Sim>: "))
                {
                    (adjusted, locked) = Rescale(tr, existing, ratios, PoleLabels.IndexLabels(tr, modelSpace));
                }

                tr.Commit();

                string own = elements.Summary(scale);
                ed.WriteMessage($"\n[SUCESSO]: Escala do desenho: 1:{scale} (texto de {DrawingScale.TextHeight(db):0.##} unidades)" +
                                (own.Length > 0 ? $"; com escala própria: {own}." : "."));
                if (adjusted > 0) ed.WriteMessage($"\n[INFO]: {adjusted} elemento(s) ajustado(s).");
                if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} elemento(s) em layer travada ficaram com o tamanho antigo.");
            }
            ed.Regen();
        }

        private class Annotations
        {
            public List<(ObjectId Id, Point3d Pole)> EffortMarkers { get; } = new List<(ObjectId, Point3d)>();
            public List<ObjectId> SpanLabels { get; } = new List<ObjectId>();
            public List<ObjectId> PoleLabels { get; } = new List<ObjectId>();
            public List<ObjectId> BoxLabels { get; } = new List<ObjectId>();
            public List<ObjectId> PoleBlocks { get; } = new List<ObjectId>();
            public List<ObjectId> BoxBlocks { get; } = new List<ObjectId>();

            /// <summary>Elementos que mudam de tamanho com essas proporções.</summary>
            public int CountChanging(Dictionary<ScaleItem, double> ratios)
            {
                int Count(ScaleItem item, int n) => Math.Abs(ratios[item] - 1) > 1e-9 ? n : 0;
                return Count(ScaleItem.Effort, EffortMarkers.Count) + Count(ScaleItem.CableText, SpanLabels.Count) +
                       Count(ScaleItem.PoleText, PoleLabels.Count) + Count(ScaleItem.BoxText, BoxLabels.Count) +
                       Count(ScaleItem.PoleIcon, PoleBlocks.Count) + Count(ScaleItem.BoxIcon, BoxBlocks.Count);
            }
        }

        /// <summary>Anotações e blocos criados pelo plugin: setas de esforço, textos dos vãos e dos postes/CTO/CEO, postes e CTO/CEO.</summary>
        private static Annotations FindAnnotations(Transaction tr, BlockTableRecord space)
        {
            var found = new Annotations();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                if (XDataTags.TryGetEffortPole(ent, out Point3d pole))
                {
                    found.EffortMarkers.Add((id, pole));
                }
                else if (ent is MText txt)
                {
                    if (txt.Layer.StartsWith(FiberSettings.CableLayerPrefix, StringComparison.OrdinalIgnoreCase))
                        found.SpanLabels.Add(id);
                    else if (txt.Layer.Equals(Core.PoleLabels.Layer, StringComparison.OrdinalIgnoreCase))
                        found.PoleLabels.Add(id);
                    else if (txt.Layer.Equals(Core.PoleLabels.BoxLayer, StringComparison.OrdinalIgnoreCase))
                        found.BoxLabels.Add(id);
                }
                else if (ent is BlockReference br)
                {
                    if (XDataTags.ReadPole(br) != null) found.PoleBlocks.Add(id);
                    else if (XDataTags.ReadBox(br) != null) found.BoxBlocks.Add(id);
                }
            }
            return found;
        }

        /// <summary>
        /// Altura dos textos mudada na janela Configurações: pergunta se ajusta os textos dos vãos e dos postes/CTO/CEO
        /// já desenhados (altura multiplicada por <paramref name="ratio"/>, mantendo o lugar de cada um).
        /// </summary>
        internal static void ResizeTexts(Editor ed, Database db, double ratio)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Annotations existing = FindAnnotations(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                int count = existing.SpanLabels.Count + existing.PoleLabels.Count + existing.BoxLabels.Count;
                if (count == 0 || !CadHelpers.AskYes(ed, $"\nAjustar a altura dos {count} textos já desenhados neste desenho? [Sim/Nao] <Sim>: "))
                {
                    tr.Commit();
                    return;
                }

                int locked = 0;
                int adjusted = RescaleSpanLabels(tr, existing.SpanLabels, ratio, ref locked) +
                               RescaleHeights(tr, existing.PoleLabels.Concat(existing.BoxLabels), ratio, ref locked);
                tr.Commit();
                ed.WriteMessage($"\n[INFO]: {adjusted} texto(s) ajustado(s).");
                if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} texto(s) em layer travada ficaram com a altura antiga.");
            }
            ed.Regen();
        }

        /// <summary>
        /// Muda o tamanho de cada tipo pela proporção dele. O que está em layer travada (o bloco, um atributo ou o texto
        /// dele) fica como estava. Retorna os ajustados e os que ficaram por estar em layer travada.
        /// </summary>
        private static (int Adjusted, int Locked) Rescale(Transaction tr, Annotations annotations, Dictionary<ScaleItem, double> ratios,
            Dictionary<string, List<ObjectId>> labels)
        {
            int adjusted = 0, locked = 0;
            bool Changes(ScaleItem item) => Math.Abs(ratios[item] - 1) > 1e-9;

            // Setas de esforço (seta + textos, ou bloco): escala em torno do centro do poste
            if (Changes(ScaleItem.Effort))
            {
                foreach (var (id, pole) in annotations.EffortMarkers)
                {
                    if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    ent.TransformBy(Matrix3d.Scaling(ratios[ScaleItem.Effort], pole));
                    adjusted++;
                }
            }
            if (Changes(ScaleItem.CableText)) adjusted += RescaleSpanLabels(tr, annotations.SpanLabels, ratios[ScaleItem.CableText], ref locked);

            // Blocos antes dos textos: o texto acompanha o bloco e depois muda de altura no lugar
            foreach (var (item, blocks) in new[] { (ScaleItem.PoleIcon, annotations.PoleBlocks), (ScaleItem.BoxIcon, annotations.BoxBlocks) })
            {
                if (!Changes(item)) continue;
                foreach (ObjectId id in blocks)
                {
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    bool anyLocked = CadHelpers.IsOnLockedLayer(tr, id) ||
                                     br.AttributeCollection.Cast<ObjectId>().Any(a => CadHelpers.IsOnLockedLayer(tr, a)) ||
                                     (labels.TryGetValue(br.Handle.ToString(), out List<ObjectId>? own) && own.Any(l => !l.IsErased && CadHelpers.IsOnLockedLayer(tr, l)));
                    if (!anyLocked && BlockSizeCommand.Scale(tr, br, ratios[item], labels)) adjusted++;
                    else locked++;
                }
            }
            if (Changes(ScaleItem.PoleText)) adjusted += RescaleHeights(tr, annotations.PoleLabels, ratios[ScaleItem.PoleText], ref locked);
            if (Changes(ScaleItem.BoxText)) adjusted += RescaleHeights(tr, annotations.BoxLabels, ratios[ScaleItem.BoxText], ref locked);
            return (adjusted, locked);
        }

        /// <summary>Textos dos vãos: altura nova e afastamento da linha proporcional, sem mudar de vão. Retorna os ajustados.</summary>
        private static int RescaleSpanLabels(Transaction tr, List<ObjectId> labels, double ratio, ref int locked)
        {
            int count = 0;
            foreach (ObjectId id in labels)
            {
                if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                var txt = (MText)tr.GetObject(id, OpenMode.ForWrite);
                double extraGap = txt.TextHeight * FiberSettings.LabelGapRatio * (ratio - 1);
                var up = new Vector3d(-Math.Sin(txt.Rotation), Math.Cos(txt.Rotation), 0);

                if (txt.Attachment == AttachmentPoint.BottomCenter) txt.Location += up * extraGap;
                else if (txt.Attachment == AttachmentPoint.TopCenter) txt.Location -= up * extraGap;

                txt.TextHeight *= ratio;
                count++;
            }
            return count;
        }

        /// <summary>Textos de identificação dos postes e CTO/CEO: só a altura, no mesmo lugar. Retorna os ajustados.</summary>
        private static int RescaleHeights(Transaction tr, IEnumerable<ObjectId> labels, double ratio, ref int locked)
        {
            int count = 0;
            foreach (ObjectId id in labels)
            {
                if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                var txt = (MText)tr.GetObject(id, OpenMode.ForWrite);
                txt.TextHeight *= ratio;
                count++;
            }
            return count;
        }
    }
}
