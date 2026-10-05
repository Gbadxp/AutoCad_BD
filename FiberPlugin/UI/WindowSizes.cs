using System.Globalization;
using System.IO;
using System.Text;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Janelas do plugin com tamanho ajustável: dá para aumentar à vontade e diminuir até 75% do tamanho original. Abaixo
    /// do tamanho original o conteúdo não espreme: aparece a barra de rolagem. O último tamanho de cada janela fica em
    /// Documentos\Fiber Plugin\janelas.txt (em pixels de 96 dpi, para valer em qualquer escala de tela).
    /// </summary>
    internal static class WindowSizes
    {
        private const double MinScale = 0.75;
        private const string FileName = "janelas.txt";
        private static Dictionary<string, string>? _saved;

        private static string FilePath => Path.Combine(PluginPaths.UserRoot, FileName);

        /// <summary>Chamado no Load, quando a janela já tem o tamanho final e a escala do DPI aplicada.</summary>
        public static void Attach(Form form)
        {
            Size design = form.ClientSize;
            Size frame = form.Size - form.ClientSize;
            // Nunca mais estreita que os botões do rodapé
            int buttons = form.Controls.OfType<FooterPanel>()
                .Select(f => f.Controls.Cast<Control>().Sum(b => b.Width + Theme.Scale(form, 8)) + Theme.Scale(form, 24))
                .DefaultIfEmpty(0).Max();
            form.MinimumSize = new Size(Math.Min(design.Width, Math.Max((int)(design.Width * MinScale), buttons)) + frame.Width,
                                        (int)(design.Height * MinScale) + frame.Height);
            MakeScrollable(form);

            float dpi = form.DeviceDpi / 96f;
            string key = $"{form.GetType().Name} {Math.Round(design.Width / dpi)}x{Math.Round(design.Height / dpi)}";
            Size Logical(Size pixels) => new Size((int)Math.Round(pixels.Width / dpi), (int)Math.Round(pixels.Height / dpi));

            // Último tamanho usado ou, sem ele, o original; nos dois casos sem passar da área útil da tela (num notebook
            // 1366x768 a janela Configurações original é mais alta que a tela e os botões ficariam atrás da barra de tarefas)
            Size wanted = Saved().TryGetValue(DataFiles.FieldKey(key), out string? text) && Parse(text) is Size saved
                ? new Size((int)Math.Round(saved.Width * dpi), (int)Math.Round(saved.Height * dpi))
                : form.Size;
            Rectangle area = Screen.FromControl(form).WorkingArea;
            var size = new Size(Math.Max(form.MinimumSize.Width, Math.Min(area.Width, wanted.Width)),
                                Math.Max(form.MinimumSize.Height, Math.Min(area.Height, wanted.Height)));
            if (size != form.Size)
            {
                form.Size = size;
                form.Location = new Point(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2);
            }

            // Grava só se o usuário mudou o tamanho (senão cada janela fechada reescreveria o arquivo)
            Size opened = Logical(form.Size);
            form.FormClosed += (s, e) =>
            {
                if (form.WindowState != FormWindowState.Normal) return;
                Size closed = Logical(form.Size);
                if (closed != opened) Save(key, closed);
            };
        }

        /// <summary>
        /// O miolo das janelas de campos (linhas de altura fixa) vai para dentro de um painel com rolagem: acima do tamanho
        /// original ele estica junto com a janela; abaixo, aparece a barra de rolagem em vez de os campos se espremerem.
        /// Cabeçalho, abas, linha de status e botões ficam sempre à vista. Janelas de lista (menu, escolha de bloco, cores)
        /// não precisam: a lista já rola e encolhe sozinha.
        /// </summary>
        private static void MakeScrollable(Form form)
        {
            Control? body = form.Controls.Cast<Control>().FirstOrDefault(c => c.Dock == DockStyle.Fill);
            if (body == null || !HasFixedRows(body)) return;

            Size design = body.Size;
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = body.BackColor, AutoScrollMinSize = design };
            int index = form.Controls.GetChildIndex(body);

            form.SuspendLayout();
            form.Controls.Remove(body);
            body.Dock = DockStyle.None;
            body.Bounds = new Rectangle(Point.Empty, design);
            host.Controls.Add(body);
            form.Controls.Add(host);
            form.Controls.SetChildIndex(host, index); // Mesma ordem de encaixe do miolo original
            form.ResumeLayout();

            // Decide pelo tamanho total do painel (com a área das barras): pela área útil, uma barra que já apareceu
            // ocuparia espaço e não sumiria mais ao voltar para o tamanho original
            host.Resize += (s, e) =>
            {
                bool wide = host.Width >= design.Width, tall = host.Height >= design.Height;
                int scrollW = SystemInformation.VerticalScrollBarWidth, scrollH = SystemInformation.HorizontalScrollBarHeight;
                // Primeiro o tamanho do conteúdo (já sem contar barras que vão sumir), depois o mínimo da rolagem
                body.Size = new Size(wide ? host.Width - (tall ? 0 : scrollW) : design.Width,
                                     tall ? host.Height - (wide ? 0 : scrollH) : design.Height);
                host.AutoScrollMinSize = new Size(wide ? 0 : design.Width, tall ? 0 : design.Height);
                if (wide && tall) host.AutoScrollPosition = Point.Empty;
                host.PerformLayout();
            };
            // O painel nasce agora, dentro de uma janela que já existe: o tema escuro vai já (o HandleCreated já passou)
            Theme.UseDarkScrollbars(host);
            Theme.ApplyDarkScrollbars(host);
        }

        private static bool HasFixedRows(Control control) =>
            control is TableLayoutPanel table && table.RowStyles.Cast<RowStyle>().Any(r => r.SizeType == SizeType.Absolute) ||
            control.Controls.Cast<Control>().Any(HasFixedRows);

        private static Dictionary<string, string> Saved()
        {
            if (_saved != null) return _saved;
            try
            {
                _saved = File.Exists(FilePath) ? DataFiles.ReadFields(FilePath) : new Dictionary<string, string>();
            }
            catch (IOException) { _saved = new Dictionary<string, string>(); }
            catch (UnauthorizedAccessException) { _saved = new Dictionary<string, string>(); }
            return _saved;
        }

        /// <summary>"900x720" → 900 x 720.</summary>
        private static Size? Parse(string text)
        {
            string[] parts = text.Split('x');
            return parts.Length == 2 && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
                   int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && w > 0 && h > 0
                ? new Size(w, h)
                : (Size?)null;
        }

        /// <summary>Grava o tamanho da janela (sem deixar de fechar se o arquivo não puder ser gravado).</summary>
        private static void Save(string key, Size logical)
        {
            Dictionary<string, string> saved = Saved();
            saved[DataFiles.FieldKey(key)] = $"{logical.Width}x{logical.Height}";
            var sb = new StringBuilder("# Fiber Plugin - último tamanho de cada janela (apague o arquivo para voltar ao original)\r\n");
            foreach (var pair in saved) sb.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
            try
            {
                Directory.CreateDirectory(PluginPaths.UserRoot);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
