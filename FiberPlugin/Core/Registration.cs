using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Cadastro na plataforma da Energisa: o que já foi cadastrado fica marcado no próprio objeto (XData de uma aplicação
    /// só dele, FIBRA_CADASTRO, que não mexe nos outros dados do plugin e vai junto no COPY). No desenho aparece como
    /// marcas por cima, em duas layers que não imprimem: verde (cadastrado) e vermelha (falta cadastrar).
    /// </summary>
    public static class Registration
    {
        public const string AppName = "FIBRA_CADASTRO";
        public const string DoneLayer = "FIBRA_CADASTRO_OK";       // Verde, não imprime
        public const string PendingLayer = "FIBRA_CADASTRO_FALTA"; // Vermelha, não imprime
        private const string DoneValue = "CADASTRADO", MarkValue = "MARCA";

        public const string Poles = "Postes", Cto = "CTO", Ceo = "CEO", Cables = "Cabos", Others = "Outros";
        public static readonly string[] Kinds = { Poles, Cto, Ceo, Cables, Others };

        /// <summary>
        /// Que tipo de objeto do projeto é (Postes, CTO, CEO, Cabos ou Outros: itens elétricos, amarrações, cordoalha e
        /// texto da reserva técnica). Null se não entra no cadastro.
        /// </summary>
        public static string? KindOf(Transaction tr, Entity ent)
        {
            switch (ent)
            {
                case BlockReference br:
                    if (XDataTags.ReadBox(br) is BoxData box) return box.Kind == BlockCategories.Ceo ? Ceo : Cto;
                    if (Core.Poles.Read(tr, br.ObjectId, br) != null) return Poles;
                    string category = BlockCategories.Of(CadHelpers.GetBlockName(tr, br));
                    return category == BlockCategories.Electrical || category == BlockCategories.Anchoring ? Others : null;
                case Polyline poly:
                    if (XDataTags.GetCableName(poly) != null) return Cables;
                    return Strand.IsStrand(poly) ? Others : null;
                case MText text:
                    return XDataTags.IsReserve(text) ? Others : null;
                default:
                    return null;
            }
        }

        public static bool IsDone(Entity ent) => Read(ent) == DoneValue;

        public static bool IsMark(Entity ent) => Read(ent) == MarkValue;

        /// <summary>Marca (ou desmarca) o objeto como cadastrado. Ele precisa estar aberto para escrita.</summary>
        public static void SetDone(Transaction tr, Database db, Entity ent, bool done) => Write(tr, db, ent, done ? DoneValue : null);

        /// <summary>
        /// Marca do objeto no desenho, na layer verde ou vermelha: em volta do bloco ou do texto um círculo; no cabo e na
        /// cordoalha uma faixa larga por cima do percurso. Null se o objeto não tem extensão.
        /// </summary>
        public static Entity? DrawMark(Transaction tr, Database db, BlockTableRecord space, Entity ent, bool done, double scale)
        {
            string layer = done ? DoneLayer : PendingLayer;
            Entity mark;
            if (ent is Polyline poly)
            {
                var band = new Polyline { Layer = layer };
                for (int i = 0; i < poly.NumberOfVertices; i++) band.AddVertexAt(i, poly.GetPoint2dAt(i), 0, 0, 0);
                band.Closed = poly.Closed;
                band.ConstantWidth = 1.5 * scale;
                mark = band;
            }
            else
            {
                Extents3d e;
                try { e = ent.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
                var center = new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2, 0);
                double radius = Math.Max(e.MinPoint.DistanceTo(e.MaxPoint) / 2, 1.0 * scale) + 0.8 * scale;
                mark = new Circle(center, Vector3d.ZAxis, radius) { Layer = layer };
            }
            CadHelpers.Append(tr, space, mark);
            Write(tr, db, mark, MarkValue);
            return mark;
        }

        private static string? Read(Entity ent)
        {
            using (ResultBuffer? rb = ent.GetXDataForApplication(AppName))
            {
                TypedValue[]? d = rb?.AsArray();
                return d != null && d.Length > 1 ? d[1].Value as string : null;
            }
        }

        /// <summary>Grava o valor na aplicação do cadastro (null = tira os dados dela), sem tocar nas outras.</summary>
        private static void Write(Transaction tr, Database db, Entity ent, string? value)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!table.Has(AppName))
            {
                table.UpgradeOpen();
                var app = new RegAppTableRecord { Name = AppName };
                table.Add(app);
                tr.AddNewlyCreatedDBObject(app, true);
            }
            var values = new List<TypedValue> { new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName) };
            if (value != null) values.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, value));
            using (var rb = new ResultBuffer(values.ToArray())) ent.XData = rb;
        }
    }
}
