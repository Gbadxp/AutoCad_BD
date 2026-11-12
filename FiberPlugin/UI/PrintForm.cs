using System.IO;
using FiberPlugin.Core;

namespace FiberPlugin.UI
{
    /// <summary>
    /// Janela Imprimir Folhas: quais folhas (layouts) vão para o PDF, um PDF com todas ou um por folha, o nome do arquivo,
    /// a pasta, colorido ou preto e branco e abrir no fim. Mostra os arquivos que vão sair enquanto o usuário escolhe.
    /// </summary>
    internal class PrintForm : Form
    {
        private readonly CheckedListBox _sheets;
        private readonly IReadOnlyList<string> _names;
        private readonly ChoiceBar _mode, _colors;
        private readonly LabeledInput _fileName, _folder;
        private readonly CheckBox _open;
        private readonly Label _files;
        private readonly StatusLabel _status;
        private readonly ThemedButton _ok;
        private bool _filling;

        /// <summary>O que foi escolhido (null enquanto faltar algo).</summary>
        public PrintSettings? Result { get; private set; }

        /// <summary>Folhas marcadas, na ordem das abas.</summary>
        public List<string> Selected { get; private set; } = new List<string>();

        /// <param name="sheets">Folhas do desenho na ordem das abas, com o tamanho do papel de cada uma.</param>
        /// <param name="checkedSheets">As que já vêm marcadas.</param>
        public PrintForm(IReadOnlyList<(string Name, string Detail)> sheets, ISet<string> checkedSheets, PrintSettings saved)
        {
            _names = sheets.Select(s => s.Name).ToList();

            Theme.ApplyForm(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Fiber Plugin - Imprimir Folhas";
            ClientSize = new Size(S(860), S(500));

            var header = new HeaderPanel
            {
                IconCommand = "FIBRA_IMPRIMIR_FOLHAS",
                Title = "Imprimir Folhas",
                Subtitle = "Gera o PDF das folhas (layouts) do desenho, cada uma com a configuração de página dela"
            };

            // ---------- Folhas ----------
            _sheets = new CheckedListBox
            {
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                Margin = new Padding(0, S(2), 0, S(4))
            };
            Theme.UseDarkScrollbars(_sheets);
            foreach (var (name, detail) in sheets)
                _sheets.Items.Add(new SheetItem(name, detail), checkedSheets.Contains(name));
            var all = new ThemedButton("Todas", false) { Margin = new Padding(0, 0, S(6), 0) };
            var none = new ThemedButton("Nenhuma", false) { Margin = Padding.Empty };
            var listButtons = new FlowLayoutPanel { WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background };
            listButtons.Controls.AddRange(new Control[] { all, none });
            var left = Column((new SectionLabel("Folhas"), 26), (_sheets, 0), (listButtons, 36));

            // ---------- Saída ----------
            _mode = new ChoiceBar("Um PDF com todas", "Um PDF por folha");
            _fileName = new LabeledInput("Nome do arquivo", "ex.: Projeto Nova Califórnia");
            _folder = new LabeledInput("Pasta", "");
            var browse = new ThemedButton("Escolher...", false) { Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(S(6), 0, 0, S(4)) };
            var folderRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _folder.Dock = DockStyle.Fill;
            folderRow.Controls.Add(_folder, 0, 0);
            folderRow.Controls.Add(browse, 1, 0);
            _files = FormLayout.Hint("");
            _colors = new ChoiceBar("Colorido", "Preto e branco");
            _open = new CheckBox { Text = "Abrir o PDF quando terminar", ForeColor = Theme.Text, BackColor = Theme.Background, Font = Theme.Body, AutoSize = true };
            _status = new StatusLabel { Margin = Padding.Empty };

            var right = Column(
                (new SectionLabel("Arquivo"), 26), (_mode, 38), (_fileName, 56), (folderRow, 56), (_files, 44),
                (new SectionLabel("Impressão"), 30), (_colors, 38), (_open, 30), (new Panel { BackColor = Theme.Background }, 0), (_status, 40));
            right.Padding = new Padding(S(16), 0, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(S(18), S(6), S(18), S(8)), BackColor = Theme.Background };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(left, 0, 0);
            body.Controls.Add(right, 1, 0);

            var footer = new FooterPanel { Hint = "Impressora DWG To PDF  ·  as escolhas ficam gravadas neste desenho" };
            _ok = new ThemedButton("Gerar PDF", true);
            var cancel = new ThemedButton("Cancelar", false) { DialogResult = DialogResult.Cancel };
            footer.AddButton(_ok);
            footer.AddButton(cancel);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;

            // ---------- Valores iniciais ----------
            _filling = true;
            _mode.SelectedIndex = saved.SinglePdf ? 0 : 1;
            _colors.SelectedIndex = saved.Monochrome ? 1 : 0;
            _fileName.Value = saved.FileName;
            _folder.Value = saved.Folder;
            _open.Checked = saved.OpenAfter;
            _filling = false;

            // ---------- Eventos ----------
            _sheets.ItemCheck += (s, e) => BeginInvoke(new Action(UpdateAll)); // O item só muda depois do evento
            all.Click += (s, e) => SetAll(true);
            none.Click += (s, e) => SetAll(false);
            _mode.SelectedChanged += (s, e) => UpdateAll();
            _colors.SelectedChanged += (s, e) => UpdateAll();
            _open.CheckedChanged += (s, e) => UpdateAll();
            foreach (LabeledInput field in new[] { _fileName, _folder }) field.Box.Input.TextChanged += (s, e) => UpdateAll();
            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { Description = "Pasta onde os PDFs vão ser gravados", ShowNewFolderButton = true })
                {
                    if (Directory.Exists(_folder.Value.Trim())) dialog.SelectedPath = _folder.Value.Trim();
                    if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Value = dialog.SelectedPath;
                }
            };
            _ok.Click += (s, e) =>
            {
                if (Result == null) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Shown += (s, e) => _ok.Focus();

            UpdateAll();
        }

        /// <summary>Pixels de 96 dpi na escala da tela.</summary>
        private int S(int pixels) => Theme.Scale(this, pixels);

        private void SetAll(bool value)
        {
            _filling = true;
            for (int i = 0; i < _sheets.Items.Count; i++) _sheets.SetItemChecked(i, value);
            _filling = false;
            UpdateAll();
        }

        private void UpdateAll()
        {
            if (_filling) return;
            Result = null;
            Selected = _names.Where((_, i) => _sheets.GetItemChecked(i)).ToList();

            bool single = _mode.SelectedIndex != 1;
            string name = _fileName.Value.Trim();
            string folder = _folder.Value.Trim();
            if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4).TrimEnd();

            string? error = null;
            if (Selected.Count == 0) error = "Marque as folhas que vão para o PDF.";
            else if (single && name.Length == 0) error = "Escreva o nome do arquivo PDF.";
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) error = "O nome do arquivo não pode ter \\ / : * ? \" < > |";
            else if (folder.Length == 0 || !Path.IsPathRooted(folder)) error = "Escolha a pasta onde os PDFs vão ser gravados.";
            else if (!single && Selected.Any(s => s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                error = "Há folha com caractere que não vale em nome de arquivo: use um PDF com todas.";

            var settings = new PrintSettings
            {
                SinglePdf = single,
                Monochrome = _colors.SelectedIndex == 1,
                OpenAfter = _open.Checked,
                FileName = name,
                Folder = folder
            };
            _files.Text = single
                ? "Todas as folhas marcadas, na ordem das abas, num arquivo só."
                : "Um arquivo para cada folha: " + (name.Length > 0 ? $"\"{name} - FL-01.pdf\"" : "\"FL-01.pdf\"") +
                  " (nome do arquivo antes do nome da folha; sem ele, só o nome da folha).";
            _ok.Enabled = error == null;
            if (error != null)
            {
                _status.Set(error, Selected.Count == 0 ? StatusKind.Hint : StatusKind.Error);
                return;
            }

            Result = settings;
            List<(string File, List<string> Sheets)> outputs = settings.Outputs(Selected);
            bool exists = outputs.Any(o => File.Exists(o.File));
            string sheets = $"{Selected.Count} folha{(Selected.Count == 1 ? "" : "s")}";
            string summary = single
                ? $"{sheets} em {Path.GetFileName(outputs[0].File)}"
                : outputs.Count == 1
                    ? $"{sheets}: {Path.GetFileName(outputs[0].File)}"
                    : $"{sheets}: {Path.GetFileName(outputs[0].File)} a {Path.GetFileName(outputs[outputs.Count - 1].File)}";
            summary += Directory.Exists(folder) ? "" : "  ·  a pasta vai ser criada";
            _status.Set(summary + (exists ? "  ·  substitui o PDF que já existe" : ""), exists ? StatusKind.Hint : StatusKind.Ok);
        }

        /// <summary>Coluna de linhas com altura fixa (0 = ocupa o resto).</summary>
        private TableLayoutPanel Column(params (Control Control, int Height)[] rows)
        {
            var column = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Background };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (control, height) in rows)
            {
                column.RowStyles.Add(height > 0 ? new RowStyle(SizeType.Absolute, S(height)) : new RowStyle(SizeType.Percent, 100));
                if (control is not CheckBox) control.Dock = DockStyle.Fill;
                column.Controls.Add(control, 0, column.RowCount++);
            }
            return column;
        }

        /// <summary>Item da lista: o nome da folha e, ao lado, o papel.</summary>
        private sealed class SheetItem
        {
            private readonly string _name, _detail;

            public SheetItem(string name, string detail)
            {
                _name = name;
                _detail = detail;
            }

            public override string ToString() => _detail.Length > 0 ? $"{_name}    ·  {_detail}" : _name;
        }
    }
}
