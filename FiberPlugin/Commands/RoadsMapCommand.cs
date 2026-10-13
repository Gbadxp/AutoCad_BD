using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Mapa de fundo do Importar Ruas, com o mapa do próprio AutoCAD (GEOMAP, que usa a geolocalização que o Importar Ruas
    /// grava): satélite, mapa de ruas ou os dois. Só na tela (não sai na impressão) ou gravado como imagem no desenho
    /// (GEOMAPIMAGE), que sai na impressão, vai para a layer FIBRA_MAPA e fica atrás das ruas. Precisa da conta Autodesk
    /// conectada no AutoCAD.
    /// </summary>
    public class RoadsMapCommand
    {
        // Imagens de mapa que já estavam no desenho antes da captura: as novas são as outras
        private static HashSet<ObjectId>? _before;
        private static string _kind = "";

        /// <param name="background">Satelite, Mapa ou Hibrido.</param>
        /// <param name="image">Grava a imagem da área no desenho.</param>
        internal static void Show(Document doc, string background, bool image, double minX, double minY, double maxX, double maxY)
        {
            (string keyword, string kind) = background switch
            {
                "Satelite" => ("_Aerial", "satélite"),
                "Mapa" => ("_Road", "mapa de ruas"),
                _ => ("_Hybrid", "satélite com ruas")
            };
            var commands = new StringBuilder($"_.GEOMAP {keyword} ");
            if (image)
            {
                _before = MapImages(doc.Database);
                _kind = kind;
                commands.Append(FormattableString.Invariant($"_.GEOMAPIMAGE *{minX:F3},{minY:F3} *{maxX:F3},{maxY:F3} "));
                commands.Append("FIBRA_MAPA_FUNDO_FIM ");
            }
            else
            {
                doc.Editor.WriteMessage($"\n[INFO]: Mapa de fundo ({kind}) ligado na tela: não sai na impressão. " +
                                        "Para desligar, comando GEOMAP > Off. Precisa da conta Autodesk conectada.");
            }
            doc.SendStringToExecute(commands.ToString(), true, false, false);
        }

        /// <summary>
        /// Depois do GEOMAPIMAGE (chamado pelo Importar Ruas): a imagem nova vai para a layer FIBRA_MAPA e para trás de
        /// tudo, e o mapa ao vivo é desligado (fica só a imagem gravada).
        /// </summary>
        [CommandMethod("FIBRA_MAPA_FUNDO_FIM")]
        public void Finish()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            HashSet<ObjectId>? before = _before;
            _before = null;
            if (before == null) return; // Só tem sentido logo depois do Importar Ruas

            int count;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite);
                List<ObjectId> created = MapImages(tr, modelSpace).Where(id => !before.Contains(id)).ToList();
                count = created.Count;
                if (count > 0)
                {
                    CadHelpers.EnsureLayer(tr, db, RoadImportSettings.MapLayer, 7);
                    foreach (ObjectId id in created)
                    {
                        if (!CadHelpers.IsOnLockedLayer(tr, id)) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Layer = RoadImportSettings.MapLayer;
                    }
                    var drawOrder = (DrawOrderTable)tr.GetObject(modelSpace.DrawOrderTableId, OpenMode.ForWrite);
                    drawOrder.MoveToBottom(new ObjectIdCollection(created.ToArray()));
                }
                tr.Commit();
            }

            if (count == 0)
            {
                ed.WriteMessage("\n[AVISO]: A imagem do mapa não foi gravada. Confira se a conta Autodesk está conectada (canto de " +
                                "cima do AutoCAD) e a internet; depois dá para gravar com o comando GEOMAPIMAGE.");
                return;
            }
            ed.WriteMessage($"\n[SUCESSO]: Imagem do mapa ({_kind}) gravada no desenho, atrás das ruas, na layer {RoadImportSettings.MapLayer}: " +
                            "sai na impressão. Congele a layer para esconder; GEOMAPIMAGEUPDATE atualiza a imagem.");
            ed.Regen();
            doc.SendStringToExecute("_.GEOMAP _Off ", true, false, false); // Fica só a imagem gravada
        }

        private static HashSet<ObjectId> MapImages(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                return new HashSet<ObjectId>(MapImages(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead)));
            }
        }

        private static IEnumerable<ObjectId> MapImages(Transaction tr, BlockTableRecord modelSpace) =>
            modelSpace.Cast<ObjectId>().Where(id => tr.GetObject(id, OpenMode.ForRead) is GeomapImage).ToList();
    }
}
