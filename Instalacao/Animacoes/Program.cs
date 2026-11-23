using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Animacoes;

/// <summary>
/// Gera um vídeo curto (MP4, sem som) para cada animação de Scenes, em FiberPlugin\Dados\Animacoes\&lt;COMANDO&gt;.mp4: a
/// dica dos botões da aba Fibra toca o vídeo (RibbonToolTip.ExpandedVideo). Opções: --out pasta, --sheets pasta (quadros
/// de conferência em PNG), --only COMANDO.
/// </summary>
internal static class Program
{
    private const int Fps = 15;
    private const double Scale = 1.5;  // 320 x 180 → 480 x 270
    private const uint Bitrate = 200_000;

    [STAThread]
    private static int Main(string[] args)
    {
        string root = FindRepo();
        string output = Arg(args, "--out") ?? Path.Combine(root, "FiberPlugin", "Dados", "Animacoes");
        string? sheets = Arg(args, "--sheets");
        string? only = Arg(args, "--only");
        Directory.CreateDirectory(output);
        if (sheets != null) Directory.CreateDirectory(sheets);

        foreach (Scene scene in Scenes.All().Where(s => only == null || s.Command.Equals(only, StringComparison.OrdinalIgnoreCase)))
        {
            string work = Path.Combine(Path.GetTempPath(), "FiberPlugin-animacao-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                int frames = (int)Math.Ceiling(scene.Duration * Fps);
                var files = new List<string>();
                for (int i = 0; i < frames; i++)
                {
                    string file = Path.Combine(work, $"q{i:D4}.png");
                    Save(Render(scene, i / (double)Fps), file);
                    files.Add(file);
                }
                string mp4 = Path.Combine(output, scene.Command + ".mp4");
                Encode(files, mp4).GetAwaiter().GetResult();
                if (sheets != null) Sheet(scene, Path.Combine(sheets, scene.Command + ".png"));
                Console.WriteLine($"{scene.Command}: {frames} quadros, {new FileInfo(mp4).Length / 1024} KB");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch (IOException) { }
            }
        }
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Pasta do repositório (a que tem a pasta FiberPlugin), subindo a partir deste programa.</summary>
    private static string FindRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "FiberPlugin", "Dados"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("Pasta FiberPlugin\\Dados não encontrada acima do gerador.");
    }

    private static BitmapSource Render(Scene scene, double t)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen()) scene.Render(dc, t);
        var bitmap = new RenderTargetBitmap((int)(Scene.W * Scale), (int)(Scene.H * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(BitmapSource image, string file)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    /// <summary>Folha de conferência: 8 quadros da animação lado a lado (2 linhas), para olhar sem abrir o vídeo.</summary>
    private static void Sheet(Scene scene, string file)
    {
        const int columns = 4, rows = 2;
        int w = (int)(Scene.W * Scale), h = (int)(Scene.H * Scale);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            for (int i = 0; i < columns * rows; i++)
            {
                double t = scene.Duration * (i + 0.5) / (columns * rows);
                dc.DrawImage(Render(scene, t), new System.Windows.Rect(i % columns * w, i / columns * h, w, h));
            }
        }
        var bitmap = new RenderTargetBitmap(columns * w, rows * h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, file);
    }

    private static async Task Encode(List<string> frames, string output)
    {
        var composition = new MediaComposition();
        foreach (string frame in frames)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(frame);
            composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(1.0 / Fps)));
        }
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(output)!);
        StorageFile target = await folder.CreateFileAsync(Path.GetFileName(output), CreationCollisionOption.ReplaceExisting);
        MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
        profile.Video.Width = (uint)(Scene.W * Scale);
        profile.Video.Height = (uint)(Scene.H * Scale);
        profile.Video.FrameRate.Numerator = Fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = Bitrate;
        profile.Audio = null;
        await composition.RenderToFileAsync(target, MediaTrimmingPreference.Precise, profile);
    }
}
