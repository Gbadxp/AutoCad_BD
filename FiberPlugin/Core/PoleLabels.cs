using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Texto de identificação do poste, ao lado do bloco:
    ///   P-12
    ///   11/300
    ///   20 L
    ///   405110.92 m E
    ///   9032585.41 m S
    /// O tipo (DT/CC) não aparece no desenho: ele só entra na listagem de postes.
    /// </summary>
    public static class PoleLabels
    {
        public const string Layer = "FIBRA_POSTES_ID";
        public const string BoxLayer = "FIBRA_CAIXAS_ID";   // Textos das CTO e CEO

        // Quebra de linha do MText
        private const string LineBreak = @"\P";

        // Afastamento do texto em relação ao ponto de inserção do bloco, na escala 1:1000
        private const double Offset = 1.5;

        // Espaço entre o desenho do bloco e o texto, na escala 1:1000
        private const double Gap = 0.5;

        /// <param name="utm">Zona do projeto; sem ela a linha "20 L" é omitida e o norte sai como hemisfério sul.</param>
        public static string Text(PoleData data, Point3d position, UtmSettings? utm)
        {
            // O ID da Energisa só aparece depois de informado (comando FIBRA_ID_ENERGISA)
            string id = data.EnergisaId.Length > 0 ? "ID " + data.EnergisaId + LineBreak : "";
            return PoleData.NumberText(data.Number) + LineBreak + id +
                   data.HeightEffort + LineBreak +
                   CoordinateLines(position, utm);
        }

        /// <summary>Texto da CTO/CEO: só a identificação (CTO-01), sem coordenadas.</summary>
        public static string BoxText(BoxData data) => data.Id;

        /// <summary>"20 L", "405110.92 m E" e "9032585.41 m S", uma por linha.</summary>
        private static string CoordinateLines(Point3d position, UtmSettings? utm)
        {
            string zone = utm != null ? UtmZone.ZoneText(position, utm) + LineBreak : "";
            return zone +
                   UtmZone.EastingText(position.X) + LineBreak +
                   UtmZone.NorthingText(position.Y, utm?.South ?? true);
        }

        /// <summary>
        /// Texto de identificação de cada poste e CTO/CEO do espaço, pelo handle do bloco. Vale para mexer em muitos blocos
        /// de uma vez (numeração, coordenadas, escala) sem varrer o desenho a cada bloco, e também acha o texto certo em
        /// desenho copiado: veja LabelMatching. Os textos repetidos exatamente em cima de outro (sobra das versões que
        /// criavam um texto novo por cima) são apagados.
        /// </summary>
        public static Dictionary<string, List<ObjectId>> IndexLabels(Transaction tr, BlockTableRecord space)
        {
            Database db = space.Database;
            var owners = new List<LabelOwner>();
            var labels = new List<LabelText>();
            foreach (ObjectId id in space)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is MText txt)
                {
                    string? owner = XDataTags.GetPoleLabelOwner(txt);
                    bool boxLayer = txt.Layer.Equals(BoxLayer, StringComparison.OrdinalIgnoreCase);
                    // Texto sem dono só entra se estiver numa das layers dos textos (versões antigas, sem o XData)
                    if (owner == null && !boxLayer && !txt.Layer.Equals(Layer, StringComparison.OrdinalIgnoreCase)) continue;
                    labels.Add(new LabelText
                    {
                        Id = id, Owner = owner ?? "", IsBox = boxLayer, At = txt.Location,
                        FirstLine = txt.Contents.Split(new[] { LineBreak }, StringSplitOptions.None)[0].Trim()
                    });
                }
                else if (obj is BlockReference br)
                {
                    string? name = XDataTags.ReadPole(br) is PoleData pole ? PoleData.NumberText(pole.Number)
                                 : XDataTags.ReadBox(br) is BoxData box ? box.Id : null;
                    if (name == null) continue;
                    Extents3d e = ExtentsOf(br, 0);
                    owners.Add(new LabelOwner
                    {
                        Handle = br.Handle.ToString(), IsBox = XDataTags.ReadBox(br) != null, Name = name,
                        MinX = e.MinPoint.X, MinY = e.MinPoint.Y, MaxX = e.MaxPoint.X, MaxY = e.MaxPoint.Y
                    });
                }
            }

            double textHeight = Math.Max(DrawingScale.TextHeight(db, ScaleItem.PoleText), DrawingScale.TextHeight(db, ScaleItem.BoxText));
            var (byOwner, duplicates) = LabelMatching.Resolve(owners, labels, reach: 3 * textHeight, tolerance: textHeight);

            foreach (int i in duplicates)
            {
                if (CadHelpers.IsOnLockedLayer(tr, labels[i].Id)) continue;
                tr.GetObject(labels[i].Id, OpenMode.ForWrite).Erase();
            }
            return byOwner.ToDictionary(p => p.Key, p => p.Value.Select(i => labels[i].Id).ToList(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Cria o texto do poste ou, se ele já existir, atualiza o conteúdo sem mudar de lugar.</summary>
        /// <param name="index">Textos já levantados (IndexLabels); null = procura no espaço.</param>
        public static void Place(Transaction tr, Database db, BlockTableRecord space, BlockReference pole, PoleData data,
            Dictionary<string, List<ObjectId>>? index = null)
        {
            PlaceText(tr, db, space, pole, Text(data, pole.Position, UtmZone.Get(db)), Layer, below: false, index);
        }

        /// <summary>Cria ou atualiza o texto da CTO/CEO, centralizado logo abaixo do símbolo.</summary>
        public static void PlaceBox(Transaction tr, Database db, BlockTableRecord space, BlockReference box, BoxData data,
            Dictionary<string, List<ObjectId>>? index = null)
        {
            PlaceText(tr, db, space, box, BoxText(data), BoxLayer, below: true, index);
        }

        /// <summary>
        /// Depois de o bloco mudar de tamanho, leva o texto junto: ele se desloca o mesmo que o ponto do
        /// desenho a que está preso, mantendo a posição que o usuário tiver dado.
        /// </summary>
        /// <param name="before">Extensão do bloco antes da mudança.</param>
        public static void Follow(Transaction tr, BlockTableRecord space, BlockReference owner, Extents3d before,
            Dictionary<string, List<ObjectId>>? index = null)
        {
            bool below = XDataTags.ReadBox(owner) != null;
            Vector3d delta = Anchor(ExtentsOf(owner, 0), below, 0) - Anchor(before, below, 0);
            foreach (MText label in LabelsOf(tr, space, owner, index))
            {
                label.UpgradeOpen();
                label.Location += delta;
                XDataTags.TagPoleLabel(tr, space.Database, label, owner.Handle.ToString()); // Conserta o vínculo de texto copiado
            }
        }

        /// <param name="below">True: centralizado abaixo do bloco (CTO/CEO). False: à direita, no alto (postes).</param>
        private static void PlaceText(Transaction tr, Database db, BlockTableRecord space, BlockReference owner, string contents,
            string layer, bool below, Dictionary<string, List<ObjectId>>? index)
        {
            List<MText> existing = LabelsOf(tr, space, owner, index);
            foreach (MText txt in existing)
            {
                txt.UpgradeOpen();
                txt.Contents = contents;
                // O texto achado pela posição (desenho copiado) passa a apontar para este bloco
                XDataTags.TagPoleLabel(tr, db, txt, owner.Handle.ToString());
            }
            if (existing.Count > 0) return;

            CadHelpers.EnsureLayer(tr, db, layer, below ? UserSettings.Current.BoxLabelColor : UserSettings.Current.PoleLabelColor);
            ScaleItem item = below ? ScaleItem.BoxText : ScaleItem.PoleText;
            double factor = DrawingScale.Factor(db, item);
            Point3d anchor = Anchor(ExtentsOf(owner, Offset * factor), below, Gap * factor);
            MText label = CadHelpers.AddText(tr, space, anchor, contents, 0,
                below ? AttachmentPoint.TopCenter : AttachmentPoint.BottomLeft, layer, DrawingScale.TextHeight(db, item));
            XDataTags.TagPoleLabel(tr, db, label, owner.Handle.ToString());
        }

        /// <summary>Texto do bloco: pelo índice já levantado ou, sem ele, levantando agora (mesma regra, veja IndexLabels).</summary>
        private static List<MText> LabelsOf(Transaction tr, BlockTableRecord space, BlockReference owner, Dictionary<string, List<ObjectId>>? index)
        {
            index ??= IndexLabels(tr, space);
            return index.TryGetValue(owner.Handle.ToString(), out List<ObjectId>? ids)
                ? ids.Where(id => !id.IsErased).Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<MText>().ToList()
                : new List<MText>();
        }

        /// <summary>Extensão do desenho do bloco (e não o ponto base, que nas CTO/CEO fica no canto).</summary>
        /// <param name="fallback">Meia largura usada se o bloco não tiver extensão.</param>
        private static Extents3d ExtentsOf(BlockReference owner, double fallback)
        {
            try
            {
                return owner.GeometricExtents;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                var half = new Vector3d(fallback, fallback, 0);
                return new Extents3d(owner.Position - half, owner.Position + half);
            }
        }

        /// <summary>Ponto do texto: centralizado abaixo do bloco ou à direita, no alto.</summary>
        private static Point3d Anchor(Extents3d e, bool below, double gap) => below
            ? new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2.0, e.MinPoint.Y - gap, 0)
            : new Point3d(e.MaxPoint.X + gap, e.MaxPoint.Y, 0);
    }
}
