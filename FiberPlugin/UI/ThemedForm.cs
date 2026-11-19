namespace FiberPlugin.UI
{
    /// <summary>
    /// Base das janelas do plugin: tema do AutoCAD (Theme.ApplyForm), escala da tela e tamanho lembrado (WindowSizes).
    /// As janelas são montadas em pixels de 96 dpi (100%) e, no Load, antes de aparecer, a janela inteira é multiplicada
    /// pela escala do Windows (125% = 1,25): tamanho, margens, linhas e colunas fixas das tabelas e os controles. A escala
    /// automática do Windows Forms não servia: ela roda no momento em que o AutoScaleMode é definido, com a janela ainda
    /// vazia, e nada do que entrava depois era escalado (no AutoCAD, em 125%, os textos cresciam e as linhas, não).
    /// </summary>
    internal class ThemedForm : Form
    {
        public ThemedForm()
        {
            Theme.ApplyForm(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            ScaleToScreen();
            WindowSizes.Attach(this);
            Size chosen = Size;
            // Centraliza (janela modal) e chama os Load: o da janela e o do AutoCAD, que no ShowModalDialog põe de volta o
            // tamanho e a posição da última vez
            base.OnLoad(e);
            if (Size != chosen) WindowSizes.Keep(this, chosen);
        }

        /// <summary>De 96 dpi para a escala da tela, uma vez só (a posição fica: a janela modal se centraliza depois).</summary>
        private void ScaleToScreen()
        {
            float factor = DeviceDpi / 96f;
            if (factor == 1f) return;
            Point location = Location;
            Scale(new SizeF(factor, factor));
            Location = location;
        }
    }
}
