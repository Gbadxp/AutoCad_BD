namespace FiberPlugin.Core
{
    /// <summary>
    /// De que lado dos postes o cabo do roteamento automático passa (coordenadas em metros, sem depender da API do
    /// AutoCAD). O lado é escolhido pela tela (cima, baixo, esquerda ou direita) e vale para a rota inteira: o cabo
    /// fica sempre do mesmo lado da rota, para não trocar de lado nas curvas. Nos postes redondos o deslocamento
    /// segue a bissetriz da rota; num poste DT, vai para a face do poste que está desse lado.
    /// </summary>
    public static class RouteSides
    {
        public const string Up = "Cima";
        public const string Down = "Baixo";
        public const string Left = "Esquerda";
        public const string Right = "Direita";
        public static readonly string[] All = { Up, Down, Left, Right };

        /// <summary>Direção na tela do lado escolhido (Cima = +Y, Direita = +X).</summary>
        public static (double X, double Y) Direction(string side) => side switch
        {
            Down => (0, -1),
            Left => (-1, 0),
            Right => (1, 0),
            _ => (0, 1)
        };

        /// <summary>Face do DT só é usada se estiver virada para o lado escolhido (até 60° fora da direção do lado).</summary>
        private const double MinFaceAlignment = 0.5; // cos 60°

        /// <summary>
        /// Direção (unitária) em que cada vértice da rota é deslocado. O lado da rota (esquerdo ou direito de quem
        /// percorre) é o que, somando o comprimento de cada vão, fica mais virado para <paramref name="side"/>.
        /// <paramref name="faces"/>: eixo das faces do poste DT em cada vértice (null nos outros). Um DT com as faces
        /// viradas para a direção do cabo (girado de lado) usaria uma face que não dá para o lado escolhido e o cabo
        /// passaria pelo meio do poste: nele vale o deslocamento normal, e o índice vai para <paramref name="sideways"/>.
        /// </summary>
        public static List<(double X, double Y)> OffsetDirections(IList<(double X, double Y)> path, (double X, double Y) side,
            IList<(double X, double Y)?>? faces = null, List<int>? sideways = null)
        {
            double score = 0;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                if (LeftNormal(path[i], path[i + 1]) is (double nx, double ny))
                    score += (nx * side.X + ny * side.Y) * PlanarMath.Distance(path[i], path[i + 1]);
            }
            int sign = score >= 0 ? 1 : -1; // +1 = esquerda de quem percorre a rota

            var result = new List<(double X, double Y)>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                var prev = i > 0 ? LeftNormal(path[i - 1], path[i]) : null;
                var next = i < path.Count - 1 ? LeftNormal(path[i], path[i + 1]) : null;

                (double X, double Y) dir = ((prev?.X ?? 0) + (next?.X ?? 0), (prev?.Y ?? 0) + (next?.Y ?? 0));
                if (Length(dir) < 1e-6) dir = prev ?? next ?? (0, 1); // Retorno de 180°
                dir = Scale(Normalize(dir), sign);

                // Poste DT: o cabo vai na face do poste (eixo das faces), na que fica do lado escolhido
                if (faces != null && i < faces.Count && faces[i] is (double fx, double fy) && Length((fx, fy)) > 1e-9)
                {
                    var face = Normalize((fx, fy));
                    double alignment = face.X * dir.X + face.Y * dir.Y;
                    if (Math.Abs(alignment) >= MinFaceAlignment) dir = alignment >= 0 ? face : Scale(face, -1);
                    else sideways?.Add(i);
                }
                result.Add(dir);
            }
            return result;
        }

        private static (double X, double Y)? LeftNormal((double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
            return length < 1e-9 ? ((double, double)?)null : (-dy / length, dx / length);
        }

        private static double Length((double X, double Y) v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
        private static (double X, double Y) Normalize((double X, double Y) v) => Scale(v, 1 / Length(v));
        private static (double X, double Y) Scale((double X, double Y) v, double k) => (v.X * k, v.Y * k);
    }
}
