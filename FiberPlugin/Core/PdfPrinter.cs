using System.Diagnostics;
using System.IO;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Converte uma página HTML em PDF com o Microsoft Edge (ou Google Chrome) em modo invisível,
    /// sem janela e sem cabeçalho/rodapé do navegador. O Edge já vem com o Windows 10 e 11.
    /// </summary>
    public static class PdfPrinter
    {
        private const int TimeoutMs = 120000;

        /// <summary>Gera o PDF. Retorna null se deu certo, ou o motivo da falha.</summary>
        public static string? Print(string htmlPath, string pdfPath)
        {
            string? browser = FindBrowser();
            if (browser == null) return "Microsoft Edge não encontrado neste computador.";

            try
            {
                if (File.Exists(pdfPath)) File.Delete(pdfPath);
            }
            catch (IOException)
            {
                return $"Não foi possível substituir {Path.GetFileName(pdfPath)}. Se ele estiver aberto, feche-o e tente de novo.";
            }

            // Perfil temporário: não interfere no Edge que o usuário estiver usando
            string profile = Path.Combine(Path.GetTempPath(), "FiberPlugin-pdf-" + Guid.NewGuid().ToString("N"));
            var psi = new ProcessStartInfo(browser)
            {
                Arguments = "--headless --disable-gpu --no-first-run --no-default-browser-check " +
                            "--no-pdf-header-footer --print-to-pdf-no-header " +
                            $"--user-data-dir=\"{profile}\" --print-to-pdf=\"{pdfPath}\" \"{new Uri(htmlPath).AbsoluteUri}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (Process? process = Process.Start(psi))
                {
                    if (process != null && !process.WaitForExit(TimeoutMs))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        return "O navegador demorou demais para gerar o PDF.";
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return $"Não foi possível abrir o navegador ({ex.Message}).";
            }
            finally
            {
                try { Directory.Delete(profile, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            return File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0 ? null : "O navegador não gerou o PDF.";
        }

        private static string? FindBrowser()
        {
            string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string x64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return new[]
            {
                Path.Combine(x86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(x64, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(x64, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(x86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe")
            }.FirstOrDefault(File.Exists);
        }
    }
}
