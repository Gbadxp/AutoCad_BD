namespace FiberPlugin.Core
{
    /// <summary>
    /// Constantes do plugin reunidas num único lugar (antes estavam espalhadas e duplicadas nos comandos).
    /// </summary>
    public static class FiberSettings
    {
        // Nome da aplicação registrada (RegApp) usada para gravar XData nas entidades do plugin
        public const string AppName = "FIBRA_PLUGIN";

        // Layers
        public const string CableLayerPrefix = "FIBRA_CABO_";
        public const string EffortLayer = "FIBRA_ESFORCOS";

        // Bloco usado para indicar o esforço no poste (buscado no desenho e depois na pasta Blocos)
        public const string EffortBlockName = "SETA DE ESFORÇO";

        // Tamanhos das anotações NA ESCALA DE REFERÊNCIA 1:1000 (2,0 = 2 mm no papel).
        // Em outra escala (comando FIBRA_ESCALA) eles são multiplicados por DrawingScale.Factor.
        // Os editáveis vêm da janela Configurações (UserSettings).

        // Textos
        public static double TextHeight => UserSettings.Current.TextHeight;
        public const double LabelGapRatio = 0.25;                         // Distância texto/linha, em alturas de texto
        public static double LabelGap => TextHeight * LabelGapRatio;     // Entre o texto do vão e a linha do cabo

        // Seta de esforço desenhada quando o desenho não tem o bloco "SETA DE ESFORÇO"
        public const double EffortArrowGap = 1.5;           // Espaço entre o centro do poste e o início da seta
        public static double EffortArrowLength => UserSettings.Current.EffortArrowLength; // Comprimento total da seta
        public const double EffortArrowHeadLength = 2.5;    // Comprimento da ponta
        public const double EffortArrowHeadWidth = 1.2;     // Largura da base da ponta

        // Símbolos de CTO/CEO: maior lado com 7 mm no papel (7 m no desenho em 1:1000, 14 m em 1:2000...).
        // Os blocos são ajustados a esse tamanho na inserção, independente da unidade em que foram
        // desenhados no BLOCOS.dwg.
        public static double BoxSymbolSize => UserSettings.Current.BoxSymbolSize;

        // Roteamento automático: afastamento do cabo em relação ao centro do poste (1,8 m por padrão)
        public static double AutoRouteOffset => UserSettings.Current.AutoRouteOffset;

        // Raio em volta do poste dentro do qual um vértice de cabo é considerado "preso" ao poste.
        // Precisa ser maior que AutoRouteOffset (o roteamento garante vértices a exatamente essa distância do poste).
        public static double PoleMatchTolerance => Math.Max(2.5, AutoRouteOffset + 0.7);

        // Raio para vincular um ponto de esforço ao poste mais próximo (Esforço no Percurso e no Poste)
        public const double PoleLinkRadius = 10.0;

        // Cálculo de tração: flecha de 1% do vão  →  T = p·L² / (8·f) = p·L / (8·0,01) = 12,5·p·L  [kgf]
        // Todo o plugin trabalha em kgf. KgfToDaN só converte o nominal do poste ("DT 11/200" está em daN).
        public const double SagRatio = 0.01;
        public const double KgfToDaN = 0.980665;
    }
}
