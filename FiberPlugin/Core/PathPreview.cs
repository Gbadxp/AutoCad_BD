using Autodesk.AutoCAD.Geometry;
using DB = Autodesk.AutoCAD.DatabaseServices;
using GI = Autodesk.AutoCAD.GraphicsInterface;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Mostra na tela o caminho já clicado enquanto o comando pede os próximos pontos, como o comando
    /// LINE faz. São gráficos temporários do AutoCAD: não entram no desenho e somem ao terminar.
    /// </summary>
    internal sealed class PathPreview : IDisposable
    {
        private readonly short _colorIndex;
        private DB.Polyline? _shown;

        public PathPreview(short colorIndex)
        {
            _colorIndex = colorIndex;
        }

        /// <summary>Redesenha o caminho com os pontos atuais (em coordenadas do mundo).</summary>
        public void Update(IList<Point3d> points)
        {
            Clear();
            if (points.Count < 2) return;

            var poly = new DB.Polyline { ColorIndex = _colorIndex };
            for (int i = 0; i < points.Count; i++)
            {
                poly.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);
            }

            GI.TransientManager.CurrentTransientManager.AddTransient(
                poly, GI.TransientDrawingMode.DirectShortTerm, 128, new IntegerCollection());
            _shown = poly;
        }

        private void Clear()
        {
            if (_shown == null) return;
            GI.TransientManager.CurrentTransientManager.EraseTransient(_shown, new IntegerCollection());
            _shown.Dispose();
            _shown = null;
        }

        public void Dispose() => Clear();
    }
}
