using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Escala do desenho e tamanho do que já está desenhado. Cada elemento do plugin tem o tamanho que a escala dele manda,
    /// o mesmo da inserção (texto: altura das Configurações × fator da escala; bloco do poste: o fator; CTO/CEO: lado do
    /// símbolo × fator; seta de esforço: o fator). Ao trocar a escala, e no Padronizar Tamanhos, tudo vai para esse tamanho,
    /// inclusive o que veio de outro desenho em outra escala ou foi mudado à mão: nada é só multiplicado.
    /// </summary>
    public class ScaleCommand
    {
        /// <summary>
        /// Define a escala do desenho. Textos dos vãos, pontos e setas de esforço passam a ser criados no tamanho certo para
        /// essa escala, e o que já está desenhado pode ir para esse tamanho na hora (também com a mesma escala: deixa do
        /// tamanho dela o que estiver diferente).
        /// </summary>
        [CommandMethod("FIBRA_ESCALA")]
        public void SetScale()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int? asked = DrawingScale.Ask(ed, "\nEscala do desenho 1:X (ex.: 500, 1000, 2000)", DrawingScale.Get(db));
            if (asked == null) return;
            Apply(ed, db, asked.Value, DrawingScale.GetElements(db));
        }

        /// <summary>
        /// Deixa todos os postes, CTO/CEO, textos, setas de esforço, reservas e cordoalhas do desenho (ou só os já
        /// selecionados) no tamanho da escala atual: o que foi colado de um desenho em outra escala fica igual ao resto.
        /// </summary>
        [CommandMethod("FIBRA_PADRONIZAR_TAMANHOS", CommandFlags.UsePickSet)]
        public void Standardize()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            PromptSelectionResult implied = ed.SelectImplied();
            HashSet<ObjectId>? only = implied.Status == PromptStatus.OK ? new HashSet<ObjectId>(implied.Value.GetObjectIds()) : null;
            int scale = DrawingScale.Get(db);
            var factors = new Targets(scale, DrawingScale.GetElements(db));

            int count, adjusted = 0, locked = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                Annotations found = FindAnnotations(tr, modelSpace, only);
                count = found.CountChanging(tr, factors);
                if (count > 0) (adjusted, locked) = Normalize(tr, found, factors, PoleLabels.IndexLabels(tr, modelSpace));
                tr.Commit();
            }

            string where = only != null ? "Dos selecionados, tudo" : "Tudo";
            if (count == 0)
            {
                ed.WriteMessage($"\n[INFO]: {where} já está no tamanho da escala 1:{scale}.");
                return;
            }
            ed.WriteMessage($"\n[SUCESSO]: {adjusted} elemento(s) no tamanho da escala 1:{scale} (texto de {DrawingScale.TextHeight(db):0.##} unidades).");
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} elemento(s) em layer travada ficaram como estavam.");
            ed.Regen();
        }

        /// <summary>
        /// Grava a escala do desenho e as escalas por elemento novas e pergunta se deixa o que já está desenhado no tamanho
        /// delas (bloco e texto dos postes, símbolo e texto das CTO/CEO, textos dos cabos, setas, reservas e cordoalhas).
        /// Usado também pela janela Configuração de Projeto.
        /// </summary>
        internal static void Apply(Editor ed, Database db, int scale, ElementScales elements)
        {
            var factors = new Targets(scale, elements);
            int before = DrawingScale.Get(db);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // O texto de cada poste é procurado pelo tamanho que ele tem agora (escala de antes): as escalas novas só
                // são gravadas depois
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                Annotations existing = FindAnnotations(tr, modelSpace);
                int count = existing.CountChanging(tr, factors);

                int adjusted = 0, locked = 0;
                if (count > 0 && CadHelpers.AskYes(ed, $"\nDeixar os {count} elementos já desenhados no tamanho da escala 1:{scale} (todos iguais, " +
                                                       "inclusive os que vieram de outro desenho)? [Sim/Nao] <Sim>: "))
                {
                    (adjusted, locked) = Normalize(tr, existing, factors, PoleLabels.IndexLabels(tr, modelSpace));
                }

                DrawingScale.Set(tr, db, scale);
                DrawingScale.SetElements(tr, db, elements);
                tr.Commit();

                string own = elements.Summary(scale);
                ed.WriteMessage(scale == before
                    ? $"\n[INFO]: Escala mantida em 1:{scale}" + (own.Length > 0 ? $"; com escala própria: {own}." : ".")
                    : $"\n[SUCESSO]: Escala do desenho: 1:{scale} (texto de {DrawingScale.TextHeight(db):0.##} unidades)" +
                      (own.Length > 0 ? $"; com escala própria: {own}." : "."));
                if (adjusted > 0) ed.WriteMessage($"\n[INFO]: {adjusted} elemento(s) no tamanho da escala.");
                if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} elemento(s) em layer travada ficaram com o tamanho antigo.");
            }
            ed.Regen();
        }

        /// <summary>
        /// Altura dos textos mudada na janela Configurações: pergunta se deixa os textos dos vãos, dos postes/CTO/CEO e da
        /// cordoalha já desenhados na altura nova (cada um no seu lugar).
        /// </summary>
        internal static void ResizeTexts(Editor ed, Database db)
        {
            var factors = new Targets(DrawingScale.Get(db), DrawingScale.GetElements(db));
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Annotations all = FindAnnotations(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                var texts = new Annotations();
                texts.SpanLabels.AddRange(all.SpanLabels);
                texts.StrandLabels.AddRange(all.StrandLabels);
                texts.PoleLabels.AddRange(all.PoleLabels);
                texts.BoxLabels.AddRange(all.BoxLabels);
                int count = texts.CountChanging(tr, factors);
                if (count == 0 || !CadHelpers.AskYes(ed, $"\nAjustar a altura dos {count} textos já desenhados neste desenho? [Sim/Nao] <Sim>: "))
                {
                    tr.Commit();
                    return;
                }

                var (adjusted, locked) = Normalize(tr, texts, factors, new Dictionary<string, List<ObjectId>>());
                tr.Commit();
                ed.WriteMessage($"\n[INFO]: {adjusted} texto(s) ajustado(s).");
                if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} texto(s) em layer travada ficaram com a altura antiga.");
            }
            ed.Regen();
        }

        /// <summary>Fatores de uma escala (1 em 1:1000): o de cada tipo de elemento e o do desenho.</summary>
        private sealed class Targets
        {
            private readonly Dictionary<ScaleItem, double> _items;

            public Targets(int scale, ElementScales elements)
            {
                _items = ElementScales.Items.ToDictionary(i => i, i => elements.Effective(i, scale) / (double)DrawingScale.Reference);
                Drawing = scale / (double)DrawingScale.Reference;
            }

            public double this[ScaleItem item] => _items[item];

            /// <summary>Fator do desenho: itens elétricos e amarrações seguem ele.</summary>
            public double Drawing { get; }
        }

        /// <summary>
        /// Tamanho padrão dos blocos mudado na janela Configurações: pergunta se deixa os postes, CTO/CEO, itens elétricos
        /// e amarrações já desenhados no tamanho novo (cada um no seu lugar, com o texto acompanhando).
        /// </summary>
        internal static void ResizeBlocks(Editor ed, Database db)
        {
            var factors = new Targets(DrawingScale.Get(db), DrawingScale.GetElements(db));
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                Annotations all = FindAnnotations(tr, modelSpace);
                var blocks = new Annotations();
                blocks.PoleBlocks.AddRange(all.PoleBlocks);
                blocks.BoxBlocks.AddRange(all.BoxBlocks);
                blocks.ElectricalBlocks.AddRange(all.ElectricalBlocks);
                blocks.AnchoringBlocks.AddRange(all.AnchoringBlocks);
                int count = blocks.CountChanging(tr, factors);
                if (count == 0 || !CadHelpers.AskYes(ed, $"\nDeixar os {count} blocos já desenhados neste desenho no tamanho novo? [Sim/Nao] <Sim>: "))
                {
                    tr.Commit();
                    return;
                }

                var (adjusted, locked) = Normalize(tr, blocks, factors, PoleLabels.IndexLabels(tr, modelSpace));
                tr.Commit();
                ed.WriteMessage($"\n[INFO]: {adjusted} bloco(s) ajustado(s).");
                if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} bloco(s) em layer travada ficaram com o tamanho antigo.");
            }
            ed.Regen();
        }

        private sealed class Annotations
        {
            public List<(ObjectId Id, Point3d Pole)> EffortMarkers { get; } = new List<(ObjectId, Point3d)>();
            public List<ObjectId> SpanLabels { get; } = new List<ObjectId>();
            public List<ObjectId> StrandLabels { get; } = new List<ObjectId>();
            public List<ObjectId> StrandLines { get; } = new List<ObjectId>();
            public List<ObjectId> BackboneLines { get; } = new List<ObjectId>();
            public List<ObjectId> ReserveTexts { get; } = new List<ObjectId>();
            public List<ObjectId> AllReserveTexts { get; } = new List<ObjectId>(); // Também os não escolhidos: a seta vai com o texto mais perto
            public List<ObjectId> ReserveArrows { get; } = new List<ObjectId>();
            public List<ObjectId> PoleLabels { get; } = new List<ObjectId>();
            public List<ObjectId> BoxLabels { get; } = new List<ObjectId>();
            public List<ObjectId> PoleBlocks { get; } = new List<ObjectId>();
            public List<ObjectId> BoxBlocks { get; } = new List<ObjectId>();
            public List<ObjectId> ElectricalBlocks { get; } = new List<ObjectId>();
            public List<ObjectId> AnchoringBlocks { get; } = new List<ObjectId>();

            /// <summary>Elementos fora do tamanho da escala (os que o ajuste muda).</summary>
            public int CountChanging(Transaction tr, Targets factors)
            {
                bool Off(double ratio) => Math.Abs(ratio - 1) > 1e-4;
                return EffortGroups(tr, this, factors).Sum(g => Off(g.Ratio) ? g.Ids.Count : 0) +
                       SpanLabels.Concat(StrandLabels).Count(id => Off(TextRatio(tr, id, factors[ScaleItem.CableText]))) +
                       ReserveGroups(tr, this, factors[ScaleItem.CableText]).Sum(g => g.Ids.Length) +
                       PoleLabels.Count(id => Off(TextRatio(tr, id, factors[ScaleItem.PoleText]))) +
                       BoxLabels.Count(id => Off(TextRatio(tr, id, factors[ScaleItem.BoxText]))) +
                       StrandLines.Count(id => Off(StrandWidthRatio(tr, id, factors[ScaleItem.CableText]))) +
                       BackboneLines.Count(id => Off(BackboneWidthRatio(tr, id, factors[ScaleItem.CableText]))) +
                       PoleBlocks.Count(id => Off(PoleBlockRatio(tr, id, factors[ScaleItem.PoleIcon]))) +
                       BoxBlocks.Count(id => Off(BoxBlockRatio(tr, id, factors[ScaleItem.BoxIcon]))) +
                       ElectricalBlocks.Count(id => Off(ElectricalBlockRatio(tr, id, factors.Drawing))) +
                       AnchoringBlocks.Count(id => Off(AnchoringBlockRatio(tr, id, factors.Drawing)));
            }
        }

        /// <summary>
        /// Anotações e blocos criados pelo plugin: setas de esforço, textos dos vãos, da cordoalha e dos postes/CTO/CEO,
        /// reservas técnicas, cordoalhas, postes, CTO/CEO, itens elétricos e amarrações. <paramref name="only"/>: só esses objetos (null = todos); de uma
        /// seta de esforço com algum pedaço selecionado vai a seta inteira, e o texto da reserva leva a seta dele.
        /// </summary>
        private static Annotations FindAnnotations(Transaction tr, BlockTableRecord space, ISet<ObjectId>? only = null)
        {
            var found = new Annotations();
            List<PoleInfo> poles = Poles.Collect(tr, space);
            var polesByHandle = Poles.ByHandle(poles);
            var markers = new List<(ObjectId Id, Point3d Pole, bool Selected)>();
            bool electrical = UserSettings.Current.ElectricalSymbolSize > 0, anchoring = UserSettings.Current.AnchoringSymbolSize > 0;
            foreach (ObjectId id in space)
            {
                bool selected = only == null || only.Contains(id);
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                // Seta copiada: escala em torno do poste de agora, não do da original
                if (XDataTags.ReadEffortMarker(ent) is EffortMarkerData marker)
                {
                    markers.Add((id, Core.EffortMarkers.Locate(ent, marker, poles, polesByHandle).Point, selected));
                }
                else if (XDataTags.TryGetEffortPole(ent, out Point3d pole))
                {
                    markers.Add((id, pole, selected));
                }
                else if (XDataTags.IsReserve(ent))
                {
                    // Todos os textos e setas: cada seta fica com o texto de reserva mais perto dela
                    if (ent is MText)
                    {
                        found.AllReserveTexts.Add(id);
                        if (selected) found.ReserveTexts.Add(id);
                    }
                    else if (ent is Polyline) found.ReserveArrows.Add(id);
                }
                else if (!selected)
                {
                    continue;
                }
                else if (ent is MText txt)
                {
                    if (txt.Layer.StartsWith(FiberSettings.CableLayerPrefix, StringComparison.OrdinalIgnoreCase) ||
                        txt.Layer.StartsWith(FiberSettings.BackboneLayerPrefix, StringComparison.OrdinalIgnoreCase))
                        found.SpanLabels.Add(id);
                    else if (txt.Layer.Equals(Strand.TextLayer, StringComparison.OrdinalIgnoreCase))
                        found.StrandLabels.Add(id);
                    else if (txt.Layer.Equals(Core.PoleLabels.Layer, StringComparison.OrdinalIgnoreCase))
                        found.PoleLabels.Add(id);
                    else if (txt.Layer.Equals(Core.PoleLabels.BoxLayer, StringComparison.OrdinalIgnoreCase))
                        found.BoxLabels.Add(id);
                }
                else if (ent is Polyline poly && Strand.IsStrand(poly))
                {
                    found.StrandLines.Add(id);
                }
                else if (ent is Polyline cable && XDataTags.IsBackbone(cable) && XDataTags.GetCableName(cable) != null)
                {
                    found.BackboneLines.Add(id);
                }
                else if (ent is BlockReference br)
                {
                    if (XDataTags.ReadPole(br) != null) found.PoleBlocks.Add(id);
                    else if (XDataTags.ReadBox(br) != null) found.BoxBlocks.Add(id);
                    else if (electrical || anchoring)
                    {
                        // Itens elétricos e amarrações só com tamanho padrão definido: sem ele ficam como estão (o
                        // tamanho de cada um pode ter sido escolhido na inserção)
                        string category = BlockCategories.Of(CadHelpers.GetBlockName(tr, br));
                        if (electrical && category == BlockCategories.Electrical) found.ElectricalBlocks.Add(id);
                        else if (anchoring && category == BlockCategories.Anchoring) found.AnchoringBlocks.Add(id);
                    }
                }
            }

            var chosen = new HashSet<(double, double)>(markers.Where(m => m.Selected).Select(m => PoleKey(m.Pole)));
            found.EffortMarkers.AddRange(markers.Where(m => chosen.Contains(PoleKey(m.Pole))).Select(m => (m.Id, m.Pole)));
            return found;
        }

        /// <summary>Ponto do poste arredondado: a seta e os textos de um mesmo ponto têm a mesma chave.</summary>
        private static (double, double) PoleKey(Point3d pole) => (Math.Round(pole.X, 3), Math.Round(pole.Y, 3));

        /// <summary>
        /// Leva cada elemento ao tamanho da escala. O que está em layer travada (o bloco, um atributo ou o texto dele) fica
        /// como estava. Retorna os ajustados e os que ficaram por estar em layer travada.
        /// </summary>
        private static (int Adjusted, int Locked) Normalize(Transaction tr, Annotations annotations, Targets factors,
            Dictionary<string, List<ObjectId>> labels)
        {
            int adjusted = 0, locked = 0;
            bool Off(double ratio) => Math.Abs(ratio - 1) > 1e-4;

            // Setas de esforço (seta + textos, ou bloco): a seta inteira muda junto, em torno do centro do poste
            foreach (var (ids, pole, ratio) in EffortGroups(tr, annotations, factors))
            {
                if (!Off(ratio)) continue;
                foreach (ObjectId id in ids)
                {
                    if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).TransformBy(Matrix3d.Scaling(ratio, pole));
                    adjusted++;
                }
            }

            double cable = factors[ScaleItem.CableText];
            adjusted += NormalizeLineTexts(tr, annotations.SpanLabels.Concat(annotations.StrandLabels), cable, ref locked);
            adjusted += NormalizeReserves(tr, annotations, cable, ref locked);
            foreach (ObjectId id in annotations.StrandLines)
            {
                if (!Off(StrandWidthRatio(tr, id, cable))) continue;
                if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                Strand.ApplyLineStyle((Polyline)tr.GetObject(id, OpenMode.ForWrite), cable);
                adjusted++;
            }

            foreach (ObjectId id in annotations.BackboneLines)
            {
                if (!Off(BackboneWidthRatio(tr, id, cable))) continue;
                if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                ((Polyline)tr.GetObject(id, OpenMode.ForWrite)).ConstantWidth = CableDrawing.BackboneWidth * cable;
                adjusted++;
            }

            // Blocos antes dos textos: o texto acompanha o bloco e depois muda de altura no lugar
            foreach (var (blocks, ratioOf) in new (List<ObjectId>, Func<ObjectId, double>)[]
                     {
                         (annotations.PoleBlocks, id => PoleBlockRatio(tr, id, factors[ScaleItem.PoleIcon])),
                         (annotations.BoxBlocks, id => BoxBlockRatio(tr, id, factors[ScaleItem.BoxIcon])),
                         (annotations.ElectricalBlocks, id => ElectricalBlockRatio(tr, id, factors.Drawing)),
                         (annotations.AnchoringBlocks, id => AnchoringBlockRatio(tr, id, factors.Drawing))
                     })
            {
                foreach (ObjectId id in blocks)
                {
                    double ratio = ratioOf(id);
                    if (!Off(ratio)) continue;
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    bool anyLocked = CadHelpers.IsOnLockedLayer(tr, id) ||
                                     br.AttributeCollection.Cast<ObjectId>().Any(a => CadHelpers.IsOnLockedLayer(tr, a)) ||
                                     (labels.TryGetValue(br.Handle.ToString(), out List<ObjectId>? own) && own.Any(l => !l.IsErased && CadHelpers.IsOnLockedLayer(tr, l)));
                    if (!anyLocked && BlockSizeCommand.Scale(tr, br, ratio, labels)) adjusted++;
                    else locked++;
                }
            }
            adjusted += NormalizeHeights(tr, annotations.PoleLabels, factors[ScaleItem.PoleText], ref locked);
            adjusted += NormalizeHeights(tr, annotations.BoxLabels, factors[ScaleItem.BoxText], ref locked);
            return (adjusted, locked);
        }

        /// <summary>
        /// Setas de esforço agrupadas pelo poste (a seta e os textos de um ponto), com a proporção que leva a seta ao tamanho
        /// da escala: pelo bloco da seta, ou pelo texto, pelo círculo do aviso "SEM POSTE" ou pelo comprimento da seta.
        /// </summary>
        private static List<(List<ObjectId> Ids, Point3d Pole, double Ratio)> EffortGroups(Transaction tr, Annotations annotations,
            Targets factors)
        {
            double f = factors[ScaleItem.Effort];
            var groups = new List<(List<ObjectId>, Point3d, double)>();
            foreach (var group in annotations.EffortMarkers.Where(m => !m.Id.IsErased).GroupBy(m => PoleKey(m.Pole)))
            {
                List<Entity> entities = group.Select(m => (Entity)tr.GetObject(m.Id, OpenMode.ForRead)).ToList();
                double? ratio = entities.OfType<BlockReference>().Select(b => Ratio(f, Math.Abs(b.ScaleFactors.X))).FirstOrDefault()
                                ?? entities.OfType<MText>().Select(t => Ratio(FiberSettings.TextHeight * f, t.TextHeight)).FirstOrDefault()
                                ?? entities.OfType<DBText>().Select(t => Ratio(FiberSettings.TextHeight * f, t.Height)).FirstOrDefault()
                                ?? entities.OfType<Circle>().Select(c => Ratio(Core.EffortMarkers.NoPoleRadius * f, c.Radius)).FirstOrDefault()
                                ?? entities.OfType<Polyline>().Select(p => Ratio(FiberSettings.EffortArrowLength * f, p.Length)).FirstOrDefault();
                if (ratio is double r) groups.Add((group.Select(m => m.Id).ToList(), group.First().Pole, r));
            }
            return groups;
        }

        /// <summary>Proporção do tamanho de agora para o certo (null se não dá para medir).</summary>
        private static double? Ratio(double target, double current) => current > 1e-9 && target > 1e-9 ? target / current : (double?)null;

        private static double TextRatio(Transaction tr, ObjectId id, double factor) =>
            !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is MText t ? Ratio(FiberSettings.TextHeight * factor, t.TextHeight) ?? 1 : 1;

        /// <summary>
        /// Bloco no tamanho padrão do grupo (Configurações > Desenho > Tamanho dos blocos) na escala, ou, sem tamanho
        /// padrão, na escala de inserção do grupo (<paramref name="unsized"/>, o tamanho do BLOCOS.dwg).
        /// </summary>
        private static double BlockRatio(Transaction tr, ObjectId id, double sizeMm, double factor, double unsized)
        {
            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            double target = BlockInsertHelpers.SymbolScale(BlockInsertHelpers.DefinitionExtents(tr, br.BlockTableRecord), sizeMm, factor, unsized);
            return Ratio(target, Math.Abs(br.ScaleFactors.X)) ?? 1;
        }

        /// <summary>Poste: o tamanho padrão ou, sem ele, o do BLOCOS.dwg na escala do ícone dos postes (como na inserção).</summary>
        private static double PoleBlockRatio(Transaction tr, ObjectId id, double factor) =>
            BlockRatio(tr, id, UserSettings.Current.PoleSymbolSize, factor, factor);

        /// <summary>Item elétrico: o tamanho padrão na escala do desenho ou, sem ele, o do BLOCOS.dwg (não acompanha a escala).</summary>
        private static double ElectricalBlockRatio(Transaction tr, ObjectId id, double drawing) =>
            BlockRatio(tr, id, UserSettings.Current.ElectricalSymbolSize, drawing, 1.0);

        /// <summary>Amarração: o tamanho padrão ou, sem ele, o do BLOCOS.dwg, na escala do desenho.</summary>
        private static double AnchoringBlockRatio(Transaction tr, ObjectId id, double drawing) =>
            BlockRatio(tr, id, UserSettings.Current.AnchoringSymbolSize, drawing, drawing);

        /// <summary>CTO/CEO: o maior lado do símbolo igual ao das Configurações × fator, como na inserção.</summary>
        private static double BoxBlockRatio(Transaction tr, ObjectId id, double factor)
        {
            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            if (BlockInsertHelpers.DefinitionExtents(tr, br.BlockTableRecord) is not Extents3d e) return 1;
            double side = Math.Max(e.MaxPoint.X - e.MinPoint.X, e.MaxPoint.Y - e.MinPoint.Y) * Math.Abs(br.ScaleFactors.X);
            return Ratio(FiberSettings.BoxSymbolSize * factor, side) ?? 1;
        }

        private static double StrandWidthRatio(Transaction tr, ObjectId id, double factor) =>
            WidthRatio(tr, id, Strand.LineWidth(factor)); // Sem largura (cordoalha da 1.9.66): muda

        private static double BackboneWidthRatio(Transaction tr, ObjectId id, double factor) =>
            WidthRatio(tr, id, CableDrawing.BackboneWidth * factor);

        /// <summary>Largura da linha em relação à da escala. Sem largura ou com larguras diferentes por trecho: vai para a da escala.</summary>
        private static double WidthRatio(Transaction tr, ObjectId id, double target)
        {
            var poly = (Polyline)tr.GetObject(id, OpenMode.ForRead);
            double width;
            try
            {
                width = poly.ConstantWidth;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                width = 0; // Larguras diferentes por trecho
            }
            return Ratio(target, width) ?? 2;
        }

        /// <summary>
        /// Textos ao longo de uma linha (vãos do cabo, trechos da cordoalha): altura da escala e afastamento da linha na
        /// mesma proporção, sem sair do vão. Retorna os ajustados.
        /// </summary>
        private static int NormalizeLineTexts(Transaction tr, IEnumerable<ObjectId> labels, double factor, ref int locked)
        {
            int count = 0;
            foreach (ObjectId id in labels)
            {
                double ratio = TextRatio(tr, id, factor);
                if (Math.Abs(ratio - 1) <= 1e-4) continue;
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

        /// <summary>
        /// Reservas técnicas: o texto na altura da escala e a seta dele na mesma proporção, em torno da ponta (que fica no
        /// poste). Retorna os ajustados.
        /// </summary>
        private static int NormalizeReserves(Transaction tr, Annotations annotations, double factor, ref int locked)
        {
            int count = 0;
            foreach (var (group, tip, ratio) in ReserveGroups(tr, annotations, factor))
            {
                if (group.Any(g => CadHelpers.IsOnLockedLayer(tr, g))) { locked += group.Length; continue; }
                foreach (ObjectId g in group) ((Entity)tr.GetObject(g, OpenMode.ForWrite)).TransformBy(Matrix3d.Scaling(ratio, tip));
                count += group.Length;
            }
            return count;
        }

        /// <summary>
        /// Reservas escolhidas fora do tamanho: o texto e a seta dele, com a ponta e a proporção. Cada seta é do texto de
        /// reserva mais perto do meio da haste (onde o texto é desenhado), contando também os textos que já estão no
        /// tamanho ou fora da seleção: assim um texto não leva a seta da reserva vizinha.
        /// </summary>
        private static List<(ObjectId[] Ids, Point3d Tip, double Ratio)> ReserveGroups(Transaction tr, Annotations annotations, double factor)
        {
            var groups = new List<(ObjectId[], Point3d, double)>();
            if (annotations.ReserveTexts.Count == 0) return groups;

            var texts = annotations.AllReserveTexts.Where(id => !id.IsErased)
                .Select(id => (Id: id, Location: ((MText)tr.GetObject(id, OpenMode.ForRead)).Location)).ToList();
            var arrowOf = new Dictionary<ObjectId, (Polyline Poly, double Distance)>();
            foreach (ObjectId id in annotations.ReserveArrows)
            {
                if (id.IsErased || texts.Count == 0) continue;
                var poly = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                if (poly.NumberOfVertices < 2) continue;
                Point3d mid = Midpoint(poly);
                var (owner, distance) = texts.Select(t => (t.Id, Distance: t.Location.DistanceTo(mid))).OrderBy(t => t.Distance).First();
                // Duas setas perto do mesmo texto: fica a mais perto
                if (!arrowOf.TryGetValue(owner, out var other) || distance < other.Distance) arrowOf[owner] = (poly, distance);
            }

            foreach (ObjectId id in annotations.ReserveTexts)
            {
                double ratio = TextRatio(tr, id, factor);
                if (Math.Abs(ratio - 1) <= 1e-4) continue;
                if (arrowOf.TryGetValue(id, out var arrow))
                    groups.Add((new[] { id, arrow.Poly.ObjectId }, arrow.Poly.GetPoint3dAt(arrow.Poly.NumberOfVertices - 1), ratio));
                else
                    groups.Add((new[] { id }, ((MText)tr.GetObject(id, OpenMode.ForRead)).Location, ratio));
            }
            return groups;
        }

        private static Point3d Midpoint(Polyline poly)
        {
            Point3d a = poly.GetPoint3dAt(0), b = poly.GetPoint3dAt(1);
            return a + (b - a) / 2;
        }

        /// <summary>Textos de identificação dos postes e CTO/CEO: só a altura, no mesmo lugar. Retorna os ajustados.</summary>
        private static int NormalizeHeights(Transaction tr, IEnumerable<ObjectId> labels, double factor, ref int locked)
        {
            int count = 0;
            foreach (ObjectId id in labels)
            {
                if (id.IsErased) continue; // Texto repetido apagado ao levantar os textos (PoleLabels.IndexLabels)
                double ratio = TextRatio(tr, id, factor);
                if (Math.Abs(ratio - 1) <= 1e-4) continue;
                if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; continue; }
                ((MText)tr.GetObject(id, OpenMode.ForWrite)).TextHeight *= ratio;
                count++;
            }
            return count;
        }
    }
}
