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
        public const string CoordinatesLayer = "FIBRA_COORDENADAS";

        // Bloco usado para indicar o esforço no poste (buscado no desenho e depois na pasta Blocos)
        public const string EffortBlockName = "SETA DE ESFORÇO";

        // Tamanhos das anotações NA ESCALA DE REFERÊNCIA 1:1000 (2,0 = 2 mm no papel).
        // Em outra escala (comando FIBRA_ESCALA) eles são multiplicados por DrawingScale.Factor.

        // Textos
        public const double TextHeight = 2.0;
        public const double LabelGap = TextHeight * 0.25;   // Distância entre o texto do vão e a linha do cabo

        // Seta de esforço desenhada quando o desenho não tem o bloco "SETA DE ESFORÇO"
        public const double EffortArrowGap = 1.5;           // Espaço entre o centro do poste e o início da seta
        public const double EffortArrowLength = 18.0;       // Comprimento total da seta
        public const double EffortArrowHeadLength = 2.5;    // Comprimento da ponta
        public const double EffortArrowHeadWidth = 1.2;     // Largura da base da ponta

        // Roteamento automático: afastamento do cabo em relação ao centro do poste
        public const double AutoRouteOffset = 1.8;

        // Raio em volta do poste dentro do qual um vértice de cabo é considerado "preso" ao poste.
        // Precisa ser maior que AutoRouteOffset (o roteamento garante vértices a exatamente 1,8 m do poste).
        public const double PoleMatchTolerance = 2.5;

        // Cálculo de tração: flecha de 1% do vão  →  T = p·L² / (8·f) = p·L / (8·0,01) = 12,5·p·L  [kgf]
        // Todo o plugin trabalha em kgf. KgfToDaN só converte o nominal do poste ("DT 11/200" está em daN).
        public const double SagRatio = 0.01;
        public const double KgfToDaN = 0.980665;
    }
}
