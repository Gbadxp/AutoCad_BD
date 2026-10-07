using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Core;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Coluna de norte e legenda das folhas do Gerar Folhas (NDU 009, item 16.3): seta do norte e os símbolos que
    /// existem no desenho (postes DT/CC, CTO, CEO, equipamentos, cabo projetado, seta de esforço), em português.
    /// </summary>
    internal sealed class SheetLegend
    {
        private const int MaxEntries = 14;
        private const double RowHeight = 10;     // mm
        private const double SymbolSize = 7;     // mm, maior lado do símbolo
        private const double LabelHeight = 2.3;  // mm

        private readonly List<(string Label, ObjectId BlockId)> _blocks = new List<(string, ObjectId)>();
        private bool _cable;
        private bool _effort;

        /// <summary>Símbolos usados no Model, na ordem: postes, caixas, demais blocos.</summary>
        public static SheetLegend Collect(Transaction tr, BlockTableRecord modelSpace)
        {
            var legend = new SheetLegend();
            var entries = new Dictionary<string, (int Order, string Label, ObjectId BlockId)>();

            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                if (ent is Polyline && XDataTags.GetCableName(ent) != null) { legend._cable = true; continue; }
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

        /// <summary>Desenha a coluna em <paramref name="area"/> (mm no papel), com as entidades na layer da moldura.</summary>
        public void Draw(Transaction tr, Database db, BlockTableRecord paperSpace, Rect area, string layer)
        {
            T Add<T>(T ent) where T : Entity
            {
                ent.Layer = layer;
                return CadHelpers.Append(tr, paperSpace, ent);
            }
            MText Text(string contents, Point3d at, double height, AttachmentPoint attachment, double width = 0) =>
                Add(new MText { Contents = contents, Location = at, TextHeight = height, Attachment = attachment, Width = width });

            double cx = (area.MinX + area.MaxX) / 2, top = area.MaxY;
            Add(new Line(new Point3d(area.MinX, area.MinY, 0), new Point3d(area.MinX, area.MaxY, 0)));

            // Norte: seta com a metade esquerda cheia e o "N" em cima (o viewport não é girado: norte para cima)
            Text("N", new Point3d(cx, top - 4, 0), 4.5, AttachmentPoint.TopCenter);
            Point3d tip = new Point3d(cx, top - 11, 0), left = new Point3d(cx - 5, top - 29, 0),
                    notch = new Point3d(cx, top - 24, 0), right = new Point3d(cx + 5, top - 29, 0);
            var outline = new Polyline { Closed = true };
            foreach (Point3d p in new[] { tip, right, notch, left }) outline.AddVertexAt(outline.NumberOfVertices, new Point2d(p.X, p.Y), 0, 0, 0);
            Add(outline);
            Add(new Solid(tip, left, notch));
            Add(new Line(new Point3d(area.MinX, top - 34, 0), new Point3d(area.MaxX, top - 34, 0)));

            // Legenda
            Text("LEGENDA", new Point3d(area.MinX + 4, top - 38, 0), 3, AttachmentPoint.TopLeft);
            double y = top - 38 - 3 - RowHeight / 2 - 2;
            double symbolX = area.MinX + 4 + SymbolSize / 2;
            double labelX = area.MinX + 4 + SymbolSize + 3;
            double labelWidth = area.MaxX - labelX - 2;

            foreach (var (label, blockId) in _blocks)
            {
                if (y - RowHeight / 2 < area.MinY) break;
                var (scale, centerOffset) = BlockInsertHelpers.FitSymbol(db, blockId, SymbolSize);
                CadHelpers.InsertBlock(tr, paperSpace, blockId, new Point3d(symbolX, y, 0) - centerOffset, 0, layer, _ => "", scale);
                Text(label, new Point3d(labelX, y, 0), LabelHeight, AttachmentPoint.MiddleLeft, labelWidth);
                y -= RowHeight;
            }

            if (_cable && y - RowHeight / 2 >= area.MinY)
            {
                Add(new Line(new Point3d(symbolX - SymbolSize / 2, y, 0), new Point3d(symbolX + SymbolSize / 2, y, 0))).ColorIndex = 3;
                Text("Cabo óptico projetado", new Point3d(labelX, y, 0), LabelHeight, AttachmentPoint.MiddleLeft, labelWidth);
                y -= RowHeight;
            }

            if (_effort && y - RowHeight / 2 >= area.MinY)
            {
                var arrow = new Polyline();
                arrow.AddVertexAt(0, new Point2d(symbolX - SymbolSize / 2, y), 0, 0, 0);
                arrow.AddVertexAt(1, new Point2d(symbolX + SymbolSize / 2 - 2, y), 0, 1.4, 0);
                arrow.AddVertexAt(2, new Point2d(symbolX + SymbolSize / 2, y), 0, 0, 0);
                Add(arrow).ColorIndex = 4;
                // Símbolo do Anexo C da NDU 009; o β vai em Arial porque as fontes SHX não têm letras gregas
                Text(@"Esforço resultante (E em daN) e ângulo ({\fArial|b0|i0|c0|p34;β})", new Point3d(labelX, y, 0), LabelHeight, AttachmentPoint.MiddleLeft, labelWidth);
            }
        }
    }
}
