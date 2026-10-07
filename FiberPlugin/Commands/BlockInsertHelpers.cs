using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>Partes comuns dos comandos de inserir (postes, CTO/CEO, elétricos, amarração...).</summary>
    internal static class BlockInsertHelpers
    {
        /// <summary>
        /// Escolhe o bloco: com um só, usa direto; com vários, mostra a janela.
        /// Null se o usuário cancelar.
        /// </summary>
        public static string? ChooseBlock(List<BlockEntry> blocks, string title, string iconCommand)
        {
            if (blocks.Count == 1) return blocks[0].Name;

            using (var form = new UI.BlockSelectionForm(blocks, title, iconCommand))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return null;
                return form.Selected?.Name;
            }
        }

        /// <summary>Copia o bloco do BLOCOS.dwg para o desenho (se preciso). ObjectId.Null com mensagem se falhar.</summary>
        public static ObjectId LoadBlock(Editor ed, Database db, string blockName)
        {
            ObjectId id = BlockRepository.EnsureInDrawing(db, blockName);
            if (id.IsNull) ed.WriteMessage($"\n[ERRO]: Não foi possível carregar o bloco '{blockName}' do {BlockRepository.LibraryFileName}.");
            return id;
        }

        /// <summary>
        /// Escala e deslocamento para o símbolo ficar com o maior lado igual a <paramref name="size"/> e
        /// centralizado no ponto clicado, seja qual for a unidade/posição em que o bloco foi desenhado
        /// (ex.: CTO desenhada em milímetros, com o ponto base no canto).
        /// Insira em (ponto clicado - CenterOffset) com a escala retornada.
        /// </summary>
        public static (double Scale, Vector3d CenterOffset) FitSymbol(Database db, ObjectId blockId, double size)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (DefinitionExtents(tr, blockId) is not Extents3d extents) return (1.0, new Vector3d(0, 0, 0));

                double longest = Math.Max(extents.MaxPoint.X - extents.MinPoint.X, extents.MaxPoint.Y - extents.MinPoint.Y);
                if (longest < 1e-9) return (1.0, new Vector3d(0, 0, 0));

                double scale = size / longest;
                var center = new Vector3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                    0);
                return (scale, center * scale);
            }
        }

        /// <summary>Extensão do desenho do bloco (sem os atributos), nas coordenadas do próprio bloco. Null se vazio.</summary>
        public static Extents3d? DefinitionExtents(Transaction tr, ObjectId blockId)
        {
            var definition = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
            Extents3d? extents = null;
            foreach (ObjectId id in definition)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent || ent is AttributeDefinition) continue;
                try
                {
                    Extents3d e = ent.GeometricExtents;
                    if (extents is Extents3d sum) { sum.AddExtents(e); extents = sum; }
                    else extents = e;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Entidade sem extensão
                }
            }
            return extents;
        }

        /// <summary>
        /// Retângulo (no mundo) que contém o símbolo do bloco inserido, sem os atributos.
        /// Sem desenho no bloco, um retângulo de tamanho zero no ponto de inserção.
        /// </summary>
        public static Extents3d SymbolBox(Transaction tr, BlockReference br)
        {
            if (DefinitionExtents(tr, br.BlockTableRecord) is not Extents3d local) return new Extents3d(br.Position, br.Position);

            // Começa no primeiro canto: um Extents3d vazio vale (0,0,0) e AddPoint incluiria a origem do desenho
            Point3d a = local.MinPoint, b = local.MaxPoint;
            Point3d[] corners = new[] { a, new Point3d(b.X, a.Y, 0), b, new Point3d(a.X, b.Y, 0) }
                .Select(c => c.TransformBy(br.BlockTransform)).ToArray();
            var box = new Extents3d(corners[0], corners[0]);
            foreach (Point3d corner in corners.Skip(1)) box.AddPoint(corner);
            return box;
        }

        /// <summary>
        /// Mostra o bloco no ponto e o gira acompanhando o mouse (como o INSERT do AutoCAD).
        /// Retorna a rotação em radianos (coordenadas do mundo): 0 com Enter, null se o usuário cancelar.
        /// </summary>
        /// <param name="scale">Escala do bloco na prévia (a mesma com que ele vai ser inserido).</param>
        public static double? DragRotation(Editor ed, ObjectId blockId, Point3d positionWcs, string prompt, double scale = 1.0)
        {
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Vector3d ucsX = ucs.CoordinateSystem3d.Xaxis;
            double ucsRotation = Math.Atan2(ucsX.Y, ucsX.X);

            using (var preview = new BlockReference(positionWcs, blockId) { ScaleFactors = new Scale3d(scale) })
            {
                var jig = new RotateBlockJig(preview, positionWcs, ucsRotation, prompt);
                PromptResult res = ed.Drag(jig);

                if (res.Status == PromptStatus.Cancel || res.Status == PromptStatus.Error) return null;
                return res.Status == PromptStatus.OK ? jig.Rotation : ucsRotation;
            }
        }

        /// <summary>Jig de rotação: o bloco gira em torno do ponto de inserção enquanto o mouse se move.</summary>
        private sealed class RotateBlockJig : EntityJig
        {
            private readonly Point3d _basePoint;
            private readonly double _ucsRotation;
            private readonly string _prompt;
            private double _angle;

            public RotateBlockJig(BlockReference block, Point3d basePoint, double ucsRotation, string prompt) : base(block)
            {
                _basePoint = basePoint;
                _ucsRotation = ucsRotation;
                _prompt = prompt;
                block.Rotation = ucsRotation;
            }

            /// <summary>Rotação final no mundo (ângulo do mouse no UCS + giro do UCS).</summary>
            public double Rotation => _angle + _ucsRotation;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptAngleOptions(_prompt)
                {
                    BasePoint = _basePoint,
                    UseBasePoint = true,
                    Cursor = CursorType.RubberBand,
                    UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted
                };

                PromptDoubleResult res = prompts.AcquireAngle(options);
                if (res.Status == PromptStatus.None) return SamplerStatus.NoChange;
                if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                if (Math.Abs(res.Value - _angle) < 1e-9) return SamplerStatus.NoChange;

                _angle = res.Value;
                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                ((BlockReference)Entity).Rotation = Rotation;
                return true;
            }
        }

        /// <summary>Mensagem quando a biblioteca não tem blocos do grupo pedido.</summary>
        public static void ReportMissing(Editor ed, string what)
        {
            ed.WriteMessage($"\n[ERRO]: Nenhum bloco de {what} no {BlockRepository.LibraryFileName}.");
            if (BlockRepository.LastError != null) ed.WriteMessage($"\n[ERRO]: {BlockRepository.LastError}");
        }
    }
}
