namespace FiberPlugin.Core
{
    /// <summary>
    /// Contas de geometria no plano (coordenadas em metros) usadas por vários comandos. Sem depender da API do
    /// AutoCAD, para servir também às partes que são testadas fora dele (ruas, nomes das ruas, amarração).
    /// </summary>
    public static class PlanarMath
    {
        public static double Distance((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>Ângulo (radianos) entre -90° e 90°, para o texto nunca ficar de cabeça para baixo.</summary>
        public static double ReadableAngle(double angle)
        {
            angle = Math.IEEERemainder(angle, 2.0 * Math.PI); // [-π, π]
            if (angle > Math.PI / 2.0 + 0.001) angle -= Math.PI;
            else if (angle < -Math.PI / 2.0 - 0.001) angle += Math.PI;
            return angle;
        }
    }
}
