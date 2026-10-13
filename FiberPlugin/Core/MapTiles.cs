using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;

namespace FiberPlugin.Core
{
    /// <summary>Servidor de blocos (tiles) de mapa na projeção Web Mercator, com o que a licença dele pede.</summary>
    public sealed class TileSource
    {
        private TileSource(string name, string urlFormat, int maxTiles, string attribution, bool jpeg, int fade, string filePrefix)
        {
            Name = name;
            UrlFormat = urlFormat;
            MaxTiles = maxTiles;
            Attribution = attribution;
            Jpeg = jpeg;
            Fade = fade;
            FilePrefix = filePrefix;
        }

        /// <summary>Nome para as mensagens.</summary>
        public string Name { get; }

        /// <summary>Endereço de um bloco: {0} = zoom, {1} = coluna (x), {2} = linha (y).</summary>
        public string UrlFormat { get; }

        /// <summary>Máximo de blocos por pedido (o zoom é o maior em que a área cabe nisso).</summary>
        public int MaxTiles { get; }

        /// <summary>Crédito que tem que ir junto do mapa no desenho.</summary>
        public string Attribution { get; }

        /// <summary>Foto (JPEG, arquivo menor) ou mapa desenhado (PNG, sem perda).</summary>
        public bool Jpeg { get; }

        /// <summary>Clareamento da imagem no desenho (0 a 100), para as ruas e cabos ficarem à frente.</summary>
        public int Fade { get; }

        public string FilePrefix { get; }

        public const int MaxZoom = 18;

        /// <summary>
        /// Mapa do OpenStreetMap (ruas, quadras e nomes). A política de uso dos servidores do OSM pede identificação do
        /// programa, no máximo 2 conexões e poucos blocos por pedido.
        /// </summary>
        public static readonly TileSource OpenStreetMap = new TileSource("mapa do OpenStreetMap",
            "https://tile.openstreetmap.org/{0}/{1}/{2}.png", 144, "Mapa: © colaboradores do OpenStreetMap", false, 25, "mapa_osm");

        /// <summary>
        /// Foto de satélite da Esri (World Imagery). Os termos da Esri permitem o uso não comercial (estudo), com o crédito
        /// junto da imagem; uso comercial pede uma conta paga da Esri.
        /// </summary>
        public static readonly TileSource EsriImagery = new TileSource("satélite da Esri",
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{0}/{2}/{1}", 484,
            "Imagem: Esri, Maxar, Earthstar Geographics e comunidade GIS (uso não comercial)", true, 10, "satelite_esri");
    }

    /// <summary>
    /// Imagem de um retângulo geográfico montada com os blocos de 256 px do servidor e cortada no retângulo. Os cantos da
    /// imagem (em lat/long) vão junto, para ela ser posicionada no desenho.
    /// </summary>
    public sealed class MapImage : IDisposable
    {
        public MapImage(TileSource source, Bitmap image, double west, double south, double east, double north, int zoom)
        {
            Source = source;
            Image = image;
            West = west;
            South = south;
            East = east;
            North = north;
            Zoom = zoom;
        }

        public TileSource Source { get; }
        public Bitmap Image { get; }
        public double West { get; }
        public double South { get; }
        public double East { get; }
        public double North { get; }
        public int Zoom { get; }

        /// <summary>Grava a imagem (JPEG ou PNG, conforme o servidor).</summary>
        public void Save(string path)
        {
            if (!Source.Jpeg)
            {
                Image.Save(path, ImageFormat.Png);
                return;
            }
            ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, 88L);
                Image.Save(path, jpeg, parameters);
            }
        }

        public void Dispose() => Image.Dispose();
    }

    /// <summary>Contas dos blocos de mapa (Web Mercator) e download da imagem de um retângulo.</summary>
    public static class MapTiles
    {
        public const int TileSize = 256;

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

        /// <summary>Maior zoom (mais detalhe) em que o retângulo cabe no máximo de blocos do servidor.</summary>
        public static int Zoom(TileSource source, double south, double west, double north, double east)
        {
            for (int zoom = TileSource.MaxZoom; zoom > 1; zoom--)
            {
                if (TileCount(south, west, north, east, zoom) <= source.MaxTiles) return zoom;
            }
            return 1;
        }

        /// <summary>
        /// Baixa os blocos (2 de cada vez) direto na imagem já cortada no retângulo. <paramref name="progress"/> recebe
        /// (baixados, total). IOException se o servidor não responder; o cancelamento interrompe na hora. Bloco que o
        /// servidor não tem (fora da cobertura) fica em branco.
        /// </summary>
        public static MapImage Download(TileSource source, double south, double west, double north, double east, CancellationToken cancel,
            Action<int, int>? progress = null)
        {
            int zoom = Zoom(source, south, west, north, east);
            int x0 = (int)Math.Floor(TileX(west, zoom)), x1 = (int)Math.Floor(TileX(east, zoom));
            int y0 = (int)Math.Floor(TileY(north, zoom)), y1 = (int)Math.Floor(TileY(south, zoom));
            int nx = x1 - x0 + 1, ny = y1 - y0 + 1, total = nx * ny, done = 0;

            // Corte no retângulo pedido (em pixels inteiros; os cantos devolvidos são os desses pixels)
            int left = (int)Math.Floor((TileX(west, zoom) - x0) * TileSize), right = (int)Math.Ceiling((TileX(east, zoom) - x0) * TileSize);
            int top = (int)Math.Floor((TileY(north, zoom) - y0) * TileSize), bottom = (int)Math.Ceiling((TileY(south, zoom) - y0) * TileSize);
            var image = new Bitmap(Math.Max(1, right - left), Math.Max(1, bottom - top), PixelFormat.Format24bppRgb);
            var gate = new object();
            try
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.White);
                    var jobs = Enumerable.Range(0, total).Select(i => (X: i % nx, Y: i / nx)).ToList();
                    var options = new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancel };
                    Parallel.ForEach(jobs, options, job =>
                    {
                        using (Bitmap? tile = Tile(source, zoom, x0 + job.X, y0 + job.Y, cancel))
                        {
                            // Cada bloco vai direto para a imagem (não guarda os blocos: área grande usaria muita memória)
                            if (tile != null) lock (gate) g.DrawImageUnscaled(tile, job.X * TileSize - left, job.Y * TileSize - top);
                        }
                        progress?.Invoke(Interlocked.Increment(ref done), total);
                    });
                }
            }
            catch (AggregateException ex) when (ex.InnerException is IOException io)
            {
                image.Dispose();
                throw io;
            }
            catch
            {
                image.Dispose();
                throw;
            }

            double px(int pixels) => x0 + pixels / (double)TileSize;
            double py(int pixels) => y0 + pixels / (double)TileSize;
            return new MapImage(source, image, Lon(px(left), zoom), Lat(py(bottom), zoom), Lon(px(right), zoom), Lat(py(top), zoom), zoom);
        }

        private static Bitmap? Tile(TileSource source, int zoom, int x, int y, CancellationToken cancel)
        {
            string url = string.Format(System.Globalization.CultureInfo.InvariantCulture, source.UrlFormat, zoom, x, y);
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
                catch (WebException ex) when (ex.Response is HttpWebResponse { StatusCode: HttpStatusCode.NotFound })
                {
                    return null; // Fora da cobertura do servidor: fica em branco
                }
                catch (System.Exception ex) when (ex is WebException || ex is IOException || ex is ArgumentException)
                {
                    cancel.ThrowIfCancellationRequested();
                    error = ex.Message;
                    if (attempt == 1 && cancel.WaitHandle.WaitOne(1500)) cancel.ThrowIfCancellationRequested();
                }
            }
            throw new IOException($"bloco {zoom}/{x}/{y} do {source.Name}: {error}");
        }
    }
}
