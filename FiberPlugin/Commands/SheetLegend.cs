using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Core;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Norte e legenda das folhas do Gerar Folhas (NDU 009, item 16.3), no quadro ao lado do carimbo: seta do norte e os
    /// símbolos que existem no desenho (postes DT/CC, CTO, CEO, equipamentos, cabo projetado, backbone, seta de esforço), em
    /// português. O lugar de cada um vem do SheetFrame (SymbolCells).
    /// </summary>
    internal sealed class SheetLegend
    {
        private const int MaxEntries = SheetFormat.MaxSymbols - 3; // Blocos; os outros três são cabo, backbone e esforço
        private const double SymbolSize = 4.6;   // mm, maior lado do símbolo (numa linha de 6 mm)
        private const double LabelHeight = 1.8;  // mm; o nome quebra em duas linhas se precisar

        private readonly List<(string Label, ObjectId BlockId)> _blocks = new List<(string, ObjectId)>();
        private bool _cable;
        private bool _backbone;
        private bool _effort;

        /// <summary>Quantos símbolos a legenda tem.</summary>
        public int Count => _blocks.Count + (_cable ? 1 : 0) + (_backbone ? 1 : 0) + (_effort ? 1 : 0);

        /// <summary>Símbolos usados no Model, na ordem: postes, caixas, demais blocos.</summary>
        public static SheetLegend Collect(Transaction tr, BlockTableRecord modelSpace)
        {
            var legend = new SheetLegend();
            var entries = new Dictionary<string, (int Order, string Label, ObjectId BlockId)>();

            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                if (ent is Polyline && XDataTags.GetCableName(ent) != null)
                {
                    if (XDataTags.IsBackbone(ent)) legend._backbone = true;
                    else legend._cable = true;
                    continue;
                }
                if (XDataTags.TryGetEffortPole(ent, out _)) { legend._effort = true; continue; }
                if (ent is not BlockReference br) continue;

                ObjectId definition = br.DynamicBlockTableRecord;
                if (XDataTags.ReadPole(br) is PoleData pole)
                {
                    string label = pole.Type == PoleData.Circular ? "Poste circular (CC)" : "Poste duplo T (DT)";
                    entries[label] = (0, label, definition);
                }
                else if (XDataTags.ReadBox(br) is BoxData box)
                {
                    string label = box.Kind == BlockCategories.Cto ? "Caixa de terminação óptica (CTO)" : "Caixa de emenda óptica (CEO)";
                    entries[label] = (1, label, definition);
                }
                else
                {
                    string name = CadHelpers.GetBlockName(tr, br);
                    string category = BlockCategories.Of(name);
                    if (category == BlockCategories.Electrical || category == BlockCategories.Anchoring)
                    {
                        string label = category == BlockCategories.Electrical ? name + " (Energisa)" : name;
                        entries[label] = (2, label, definition);
                    }
                }
            }

            legend._blocks.AddRange(entries.Values.OrderBy(e => e.Order).ThenBy(e => e.Label).Take(MaxEntries).Select(e => (e.Label, e.BlockId)));
            return legend;
        }

        /// <summary>
        /// Desenha a seta do norte em <paramref name="north"/> e cada símbolo, com o nome ao lado, no lugar dele em
        /// <paramref name="cells"/> (mm no papel), com as entidades na layer da moldura.
        /// </summary>
        public void Draw(Transaction tr, Database db, BlockTableRecord paperSpace, Rect north, IReadOnlyList<Rect> cells, string layer)
        {
            T Add<T>(T ent) where T : Entity
            {
                ent.Layer = layer;
                return CadHelpers.Append(tr, paperSpace, ent);
            }
            MText Text(string contents, Point3d at, double height, AttachmentPoint attachment, double width = 0) =>
                Add(new MText { Contents = contents, Location = at, TextHeight = height, Attachment = attachment, Width = width });

            // Norte: o "N" em cima e a seta com a metade esquerda cheia (o viewport não é girado: norte para cima), do
            // tamanho que couber na altura do quadro
            double cx = north.CenterX, top = north.MaxY;
            double letter = Math.Min(4.5, north.Height * 0.12);
            Text("N", new Point3d(cx, top - 2, 0), letter, AttachmentPoint.TopCenter);
            double tipY = top - 2 - letter - 2.5, length = Math.Max(6, Math.Min(18, tipY - north.MinY - 3)), half = length * 5 / 18;
            Point3d tip = new Point3d(cx, tipY, 0), left = new Point3d(cx - half, tipY - length, 0),
                    notch = new Point3d(cx, tipY - length * 13 / 18, 0), right = new Point3d(cx + half, tipY - length, 0);
            var outline = new Polyline { Closed = true };
            foreach (Point3d p in new[] { tip, right, notch, left }) outline.AddVertexAt(outline.NumberOfVertices, new Point2d(p.X, p.Y), 0, 0, 0);
            Add(outline);
            Add(new Solid(tip, left, notch));

            // Os símbolos, na ordem: blocos, cabo, backbone e esforço; cada um no lugar que o SheetFrame deu
            var entries = new List<(string Label, Action<Point3d, double> DrawSymbol)>();
            foreach (var (label, blockId) in _blocks)
            {
                entries.Add((label, (at, size) =>
                {
                    var (scale, centerOffset) = BlockInsertHelpers.FitSymbol(db, blockId, size);
                    CadHelpers.InsertBlock(tr, paperSpace, blockId, at - centerOffset, 0, layer, _ => "", scale);
                }));
            }
            // Cabo e backbone nas cores das Configurações (a cor própria de um cabo na planilha não entra aqui)
            if (_cable)
                entries.Add(("Cabo óptico projetado", (at, size) =>
                    Add(new Line(new Point3d(at.X - size / 2, at.Y, 0), new Point3d(at.X + size / 2, at.Y, 0))).ColorIndex = UserSettings.Current.CableColor));
            if (_backbone)
            {
                entries.Add(("Cabo óptico tronco (backbone)", (at, size) =>
                {
                    var line = new Polyline();
                    line.AddVertexAt(0, new Point2d(at.X - size / 2, at.Y), 0, 0, 0);
                    line.AddVertexAt(1, new Point2d(at.X + size / 2, at.Y), 0, 0, 0);
                    line.ConstantWidth = CableDrawing.BackboneWidth; // Depois dos vértices; em mm, como no desenho em 1:1000
                    Add(line).ColorIndex = UserSettings.Current.BackboneColor;
                }));
            }
            if (_effort)
            {
                entries.Add(("Esforço no poste (kgf e ângulo)", (at, size) =>
                {
                    var arrow = new Polyline();
                    arrow.AddVertexAt(0, new Point2d(at.X - size / 2, at.Y), 0, 0, 0);
                    arrow.AddVertexAt(1, new Point2d(at.X + size / 2 - size * 0.28, at.Y), 0, size * 0.2, 0);
                    arrow.AddVertexAt(2, new Point2d(at.X + size / 2, at.Y), 0, 0, 0);
                    Add(arrow).ColorIndex = UserSettings.Current.EffortColor; // A cor das setas no desenho (Configurações)
                }));
            }

            for (int i = 0; i < entries.Count && i < cells.Count; i++)
            {
                Rect cell = cells[i];
                double size = Math.Min(SymbolSize, cell.Height * 0.8), y = cell.CenterY;
                double labelX = cell.MinX + 2 + size + 2;
                entries[i].DrawSymbol(new Point3d(cell.MinX + 2 + size / 2, y, 0), size);
                // O nome pode ser o de um bloco do usuário: texto puro, sem virar código de formatação do MText
                Text(CadHelpers.MTextLiteral(entries[i].Label), new Point3d(labelX, y, 0), LabelHeight, AttachmentPoint.MiddleLeft, cell.MaxX - labelX - 1);
            }
        }
    }
}
