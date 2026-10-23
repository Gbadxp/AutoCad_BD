using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Mapa de fundo do Importar Ruas: a imagem do mapa do OpenStreetMap ou do satélite da Esri, baixada pelo plugin e
    /// colocada no desenho atrás das ruas, no mesmo lugar delas (layer FIBRA_MAPA, sai na impressão), sem conta nenhuma.
    /// O mapa do próprio AutoCAD (GEOMAP) não é usado: precisa da conta Autodesk; aqui só fica a proteção que o desliga em
    /// desenho aberto sem o login, quando o AutoCAD trava tentando baixar.
    /// </summary>
    public static class BackgroundMap
    {
        /// <summary>
        /// Coloca a imagem do mapa (OpenStreetMap ou satélite da Esri) no desenho, atrás de tudo, na layer FIBRA_MAPA: o
        /// arquivo vai para a pasta "&lt;desenho&gt;_mapas" ao lado do DWG (ou Documentos\Fiber Plugin\Mapas, com o desenho
        /// ainda sem salvar) e a imagem é posicionada pelos três cantos convertidos para a UTM do projeto, então fica em
        /// cima das ruas. A imagem e o crédito que o plugin tinha posto antes na mesma área saem. Retorna o arquivo gravado.
        /// </summary>
        internal static string InsertTiles(Document doc, MapImage map, UtmSettings utm, double minX, double minY, double maxX, double maxY)
        {
            Database db = doc.Database;
            string folder = Path.IsPathRooted(doc.Name) && Directory.Exists(Path.GetDirectoryName(doc.Name))
                ? Path.Combine(Path.GetDirectoryName(doc.Name)!, Path.GetFileNameWithoutExtension(doc.Name) + "_mapas")
                : Path.Combine(PluginPaths.UserRoot, "Mapas");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"{map.Source.FilePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.{(map.Source.Jpeg ? "jpg" : "png")}");
            map.Save(file);

            // Cantos da imagem (lat/long exatos dos pixels) na UTM do projeto: de baixo à esquerda, de baixo à direita e
            // de cima à esquerda. Numa área de alguns km o mapa do OSM (Mercator) fica a menos de 1 m da UTM assim.
            Point3d Utm(double lat, double lon)
            {
                var (e, n) = UtmZone.FromGeographic(lat, lon, utm.Zone, utm.South);
                return new Point3d(e, n, 0);
            }
            Point3d bottomLeft = Utm(map.South, map.West), bottomRight = Utm(map.South, map.East), topLeft = Utm(map.North, map.West);

            try
            {
                // Módulo de imagens do AutoCAD (o IMAGEATTACH carrega sozinho; pela API às vezes ainda não está carregado)
                SystemObjects.DynamicLinker.LoadModule($"acismobj{AcApp.Version.Major}.dbx", false, false);
            }
            catch (System.Exception)
            {
                // Já carregado, ou com outro nome nesta versão: a criação da imagem diz se faltar
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                CadHelpers.EnsureLayer(tr, db, RoadImportSettings.MapLayer, 7);

                // Mapa anterior do plugin na mesma área: sai (o arquivo dele fica na pasta)
                CadHelpers.EraseUnlocked(tr, modelSpace.Cast<ObjectId>()
                    .Where(id => tr.GetObject(id, OpenMode.ForRead) is Entity ent && XDataTags.IsMap(ent) && ent is not GeomapImage &&
                                 Overlaps(ent, minX, minY, maxX, maxY))
                    .ToList());

                ObjectId dictionaryId = RasterImageDef.GetImageDictionary(db);
                if (dictionaryId.IsNull) dictionaryId = RasterImageDef.CreateImageDictionary(db);
                var dictionary = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForWrite);
                var definition = new RasterImageDef { SourceFileName = file };
                definition.Load();
                ObjectId definitionId = dictionary.SetAt(RasterImageDef.SuggestName(dictionary, file), definition);
                tr.AddNewlyCreatedDBObject(definition, true);

                var image = new RasterImage
                {
                    ImageDefId = definitionId,
                    Orientation = new CoordinateSystem3d(bottomLeft, bottomRight - bottomLeft, topLeft - bottomLeft),
                    ShowImage = true,
                    Layer = RoadImportSettings.MapLayer,
                    Fade = (byte)map.Source.Fade // Um pouco clara: as ruas, postes e cabos desenhados ficam à frente
                };
                CadHelpers.Append(tr, modelSpace, image);
                RasterImage.EnableReactors(true);
                image.AssociateRasterDef(definition);
                XDataTags.TagMap(tr, db, image);

                // Crédito do mapa (a licença exige junto da imagem), no canto de baixo à direita da área
                double height = DrawingScale.TextHeight(db);
                MText credit = CadHelpers.AddText(tr, modelSpace, new Point3d(maxX - height, minY + height, 0),
                    CadHelpers.MTextLiteral(map.Source.Attribution), 0, AttachmentPoint.BottomRight, RoadImportSettings.MapLayer, height);
                XDataTags.TagMap(tr, db, credit);

                var drawOrder = (DrawOrderTable)tr.GetObject(modelSpace.DrawOrderTableId, OpenMode.ForWrite);
                drawOrder.MoveToBottom(new ObjectIdCollection(new[] { image.ObjectId }));
                tr.Commit();
            }
            return file;
        }

        private static bool Overlaps(Entity ent, double minX, double minY, double maxX, double maxY)
        {
            try
            {
                Extents3d e = ent.GeometricExtents;
                return e.MinPoint.X <= maxX && e.MaxPoint.X >= minX && e.MinPoint.Y <= maxY && e.MaxPoint.Y >= minY;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// AutoCAD conectado à conta Autodesk: o mapa do AutoCAD (GEOMAP) só funciona assim, e ligado sem o login o AutoCAD
        /// fica tentando e pode travar. Null se não deu para saber.
        /// </summary>
        internal static bool? SignedIn()
        {
            // Variável de sistema com o usuário da conta Autodesk (vazia sem login) e o estado do login do AutoCAD: basta
            // um dos dois dizer que está conectado; sem login só quando nenhum diz que está
            bool? byUserId = null, byService = null;
            try
            {
                if (AcApp.GetSystemVariable("ONLINEUSERID") is string id) byUserId = id.Trim().Length > 0;
            }
            catch (System.Exception)
            {
                // Variável que não existe nesta versão
            }
            if (byUserId == true) return true;
            try
            {
                byService = Autodesk.AutoCAD.Internal.WSUtils.IsLoggedIn();
            }
            catch (System.Exception)
            {
                // API interna que mudou nesta versão
            }
            if (byService == true) return true;
            return byUserId == false || byService == false ? false : (bool?)null;
        }

        /// <summary>
        /// Desenho aberto com o mapa ao vivo ligado e o AutoCAD sem login: o mapa não aparece e o AutoCAD fica tentando
        /// baixar (trava). Desliga o mapa e avisa. Chamado ao ativar desenhos e no Importar Ruas.
        /// </summary>
        internal static void TurnOffWithoutSignIn(Document? doc)
        {
            if (doc == null) return;
            try
            {
                if (Convert.ToInt32(AcApp.GetSystemVariable("GEOMAPMODE")) == 0 || SignedIn() != false) return;
            }
            catch (System.Exception)
            {
                return; // Sem a variável do mapa: nada a fazer
            }
            try
            {
                AcApp.SetSystemVariable("GEOMAPMODE", (short)0);
            }
            catch (System.Exception)
            {
                doc.SendStringToExecute("_.GEOMAP _Off ", true, false, false);
            }
            doc.Editor.WriteMessage("\n[AVISO]: Este desenho estava com o mapa de fundo do AutoCAD (GEOMAP) ligado, mas o AutoCAD não " +
                                    "está conectado à conta Autodesk (o mapa não aparece e o AutoCAD fica travando tentando): o mapa " +
                                    "foi desligado. Para mapa de fundo sem conta, use o Importar Ruas (OpenStreetMap ou satélite da Esri).\n");
        }
    }
}
