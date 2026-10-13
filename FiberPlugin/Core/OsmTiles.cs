using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Imagem do mapa do OpenStreetMap de um retângulo geográfico, montada com os blocos (tiles) de 256 px do servidor
    /// do OSM e cortada no retângulo. Os cantos da imagem (em lat/long) vão junto, para ela ser posicionada no desenho.
    /// </summary>
    public sealed class MapImage : IDisposable
    {
        public MapImage(Bitmap image, double west, double south, double east, double north, int zoom)
        {
            Image = image;
            West = west;
            South = south;
            East = east;
            North = north;
            Zoom = zoom;
        }

        public Bitmap Image { get; }
        public double West { get; }
        public double South { get; }
        public double East { get; }
        public double North { get; }
        public int Zoom { get; }

        public void Dispose() => Image.Dispose();
    }

    /// <summary>
    /// Blocos do mapa do OpenStreetMap (tile.openstreetmap.org), na projeção Web Mercator. Segue a política de uso dos
    /// servidores do OSM: identificação do programa, no máximo 2 conexões e poucos blocos por pedido (até 144: área de
    /// alguns km no zoom que couber). O crédito "© colaboradores do OpenStreetMap" tem que aparecer junto do mapa.
    /// </summary>
    public static class OsmTiles
    {
        public const string Attribution = "Mapa: © colaboradores do OpenStreetMap";
        public const int TileSize = 256;
        public const int MaxTiles = 144;
        public const int MaxZoom = 18;
        private const string Server = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";

        public static double TileX(double lon, int zoom) => (lon + 180.0) / 360.0 * (1 << zoom);

        public static double TileY(double lat, int zoom)
        {
            double r = lat * Math.PI / 180.0;
            return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * (1 << zoom);
        }

        public static double Lon(double tileX, int zoom) => tileX / (1 << zoom) * 360.0 - 180.0;

        public static double Lat(double tileY, int zoom) => Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * tileY / (1 << zoom)))) * 180.0 / Math.PI;

        /// <summary>Quantos blocos o retângulo ocupa no zoom.</summary>
        public static int TileCount(double south, double west, double north, double east, int zoom)
        {
            int nx = (int)Math.Floor(TileX(east, zoom)) - (int)Math.Floor(TileX(west, zoom)) + 1;
            int ny = (int)Math.Floor(TileY(south, zoom)) - (int)Math.Floor(TileY(north, zoom)) + 1;
            return nx * ny;
        }

        /// <summary>Maior zoom (mais detalhe) em que o retângulo cabe em até <see cref="MaxTiles"/> blocos.</summary>
        public static int Zoom(double south, double west, double north, double east)
        {
            for (int zoom = MaxZoom; zoom > 1; zoom--)
            {
                if (TileCount(south, west, north, east, zoom) <= MaxTiles) return zoom;
            }
            return 1;
        }

        /// <summary>
        /// Baixa os blocos (2 de cada vez), monta a imagem e corta no retângulo. <paramref name="progress"/> recebe
        /// (baixados, total). IOException se o servidor não responder; o cancelamento interrompe na hora.
        /// </summary>
        public static MapImage Download(double south, double west, double north, double east, CancellationToken cancel,
            Action<int, int>? progress = null)
        {
            int zoom = Zoom(south, west, north, east);
            int x0 = (int)Math.Floor(TileX(west, zoom)), x1 = (int)Math.Floor(TileX(east, zoom));
            int y0 = (int)Math.Floor(TileY(north, zoom)), y1 = (int)Math.Floor(TileY(south, zoom));
            int nx = x1 - x0 + 1, ny = y1 - y0 + 1, total = nx * ny, done = 0;

            var tiles = new Bitmap?[nx, ny];
            try
            {
                var jobs = Enumerable.Range(0, total).Select(i => (X: i % nx, Y: i / nx)).ToList();
                var options = new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancel };
                Parallel.ForEach(jobs, options, job =>
                {
                    tiles[job.X, job.Y] = Tile(zoom, x0 + job.X, y0 + job.Y, cancel);
                    progress?.Invoke(Interlocked.Increment(ref done), total);
                });
            }
            catch (AggregateException ex) when (ex.InnerException is IOException io)
            {
                DisposeAll(tiles);
                throw io;
            }
            catch
            {
                DisposeAll(tiles);
                throw;
            }

            // Mosaico e corte no retângulo pedido (em pixels inteiros; os cantos devolvidos são os desses pixels)
            int left = (int)Math.Floor((TileX(west, zoom) - x0) * TileSize), right = (int)Math.Ceiling((TileX(east, zoom) - x0) * TileSize);
            int top = (int)Math.Floor((TileY(north, zoom) - y0) * TileSize), bottom = (int)Math.Ceiling((TileY(south, zoom) - y0) * TileSize);
            var image = new Bitmap(Math.Max(1, right - left), Math.Max(1, bottom - top), PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.White);
                for (int i = 0; i < nx; i++)
                {
                    for (int j = 0; j < ny; j++)
                    {
                        if (tiles[i, j] is Bitmap tile) g.DrawImageUnscaled(tile, i * TileSize - left, j * TileSize - top);
                    }
                }
            }
            DisposeAll(tiles);

            double px(int pixels) => x0 + pixels / (double)TileSize;
            double py(int pixels) => y0 + pixels / (double)TileSize;
            return new MapImage(image, Lon(px(left), zoom), Lat(py(bottom), zoom), Lon(px(right), zoom), Lat(py(top), zoom), zoom);
        }

        private static Bitmap Tile(int zoom, int x, int y, CancellationToken cancel)
        {
            string url = string.Format(System.Globalization.CultureInfo.InvariantCulture, Server, zoom, x, y);
            string error = "";
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
#pragma warning disable SYSLIB0014 // WebRequest existe nas duas compilações (net48 não tem HttpClient sem referência extra)
                var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
                request.UserAgent = $"FiberPlugin/{PluginInfo.Version} (AutoCAD; mapa de fundo do Importar Ruas)";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                try
                {
                    using (cancel.Register(request.Abort))
                    using (WebResponse response = request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        memory.Position = 0;
                        using (var decoded = new Bitmap(memory)) return new Bitmap(decoded); // Cópia: não depende do stream
                    }
                }
                catch (System.Exception ex) when (ex is WebException || ex is IOException || ex is ArgumentException)
                {
                    cancel.ThrowIfCancellationRequested();
                    error = ex.Message;
                    if (attempt == 1 && cancel.WaitHandle.WaitOne(1500)) cancel.ThrowIfCancellationRequested();
                }
            }
            throw new IOException($"bloco {zoom}/{x}/{y} do OpenStreetMap: {error}");
        }

        private static void DisposeAll(Bitmap?[,] tiles)
        {
            foreach (Bitmap? tile in tiles) tile?.Dispose();
        }
    }
}
