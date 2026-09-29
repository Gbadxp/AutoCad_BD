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

            // O navegador grava num arquivo temporário (perfil próprio, sem mexer no Edge do usuário);
            // só no fim ele é copiado para o destino, que pode estar aberto num leitor de PDF
            string work = Path.Combine(Path.GetTempPath(), "FiberPlugin-pdf-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(work, "perfil");
            string tempPdf = Path.Combine(work, "memorial.pdf");
            Directory.CreateDirectory(work);
            var psi = new ProcessStartInfo(browser)
            {
                Arguments = "--headless --disable-gpu --no-first-run --no-default-browser-check " +
                            "--no-pdf-header-footer --print-to-pdf-no-header " +
                            $"--user-data-dir=\"{profile}\" --print-to-pdf=\"{tempPdf}\" \"{new Uri(htmlPath).AbsoluteUri}\"",
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
                if (!File.Exists(tempPdf) || new FileInfo(tempPdf).Length == 0) return "O navegador não gerou o PDF.";

                File.Copy(tempPdf, pdfPath, true);
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return $"Não foi possível abrir o navegador ({ex.Message}).";
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return $"Não foi possível gravar {Path.GetFileName(pdfPath)}. Se ele estiver aberto, feche-o e tente de novo.";
            }
            finally
            {
                try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
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
