using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>Dados gravados na CTO/CEO pelo FIBRA_INSERIR_CTO / FIBRA_INSERIR_CEO.</summary>
    public class BoxData
    {
        public string Kind { get; set; } = BlockCategories.Cto;   // "CTO" ou "CEO"
        public int Number { get; set; }
        public string PoleHandle { get; set; } = "";              // Poste mais próximo na inserção ("" = nenhum)

        /// <summary>Identificação: CTO-01, CEO-03...</summary>
        public string Id => Kind + "-" + Number.ToString("D2", CultureInfo.InvariantCulture);
    }

    public class BoxInfo
    {
        public ObjectId Id { get; set; }
        public Point3d Position { get; set; }
        public string BlockName { get; set; } = "";
        public BoxData Data { get; set; } = new BoxData();
    }

    public static class Boxes
    {
        /// <summary>Centro do desenho do símbolo (o ponto base dos blocos de CTO/CEO pode estar no canto).</summary>
        private static Point3d Center(BlockReference br)
        {
            try
            {
                Extents3d e = br.GeometricExtents;
                return new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2.0, (e.MinPoint.Y + e.MaxPoint.Y) / 2.0, 0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return br.Position;
            }
        }

        /// <summary>CTO e CEO inseridas pelo plugin.</summary>
        public static List<BoxInfo> Collect(Transaction tr, BlockTableRecord space)
        {
            var boxes = new List<BoxInfo>();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br) continue;

                BoxData? data = XDataTags.ReadBox(br);
                if (data == null) continue;

                boxes.Add(new BoxInfo
                {
                    Id = id,
                    Position = Center(br),
                    BlockName = CadHelpers.GetBlockName(tr, br),
                    Data = data
                });
            }
            return boxes;
        }
    }
}
