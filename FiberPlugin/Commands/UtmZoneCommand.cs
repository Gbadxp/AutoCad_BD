using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class UtmZoneCommand
    {
        /// <summary>
        /// Define a zona UTM e o hemisfério do projeto (gravados no DWG) e atualiza as coordenadas dos postes e dos
        /// demais blocos, que passam a mostrar a posição atual de cada um.
        /// </summary>
        [CommandMethod("FIBRA_ZONA_UTM")]
        public void SetUtmZone()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            UtmSettings? utm = UtmZone.Ask(ed, db, UtmZone.Get(db));
            if (utm == null) return;

            ed.WriteMessage($"\n[SUCESSO]: Zona UTM do projeto: {utm.Zone}, hemisfério {(utm.South ? "Sul" : "Norte")}.");
            UpdateCoordinates(ed, db, utm);
        }

        /// <summary>
        /// Projeto levado para outro lugar (ou postes movidos): reescreve as coordenadas de todos os blocos com a posição
        /// atual de cada um, na zona do projeto. Sem zona definida, pergunta.
        /// </summary>
        [CommandMethod("FIBRA_ATUALIZAR_COORDENADAS")]
        public void RefreshCoordinates()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            UtmSettings? utm = UtmZone.Get(db) ?? UtmZone.Ask(ed, db, null);
            if (utm == null) return;
            UpdateCoordinates(ed, db, utm);
        }

        /// <summary>
        /// Atualiza, sem perguntar, as coordenadas pela posição atual de cada bloco: o texto dos postes (zona com a letra
        /// da faixa, E e N) e os atributos COORDENADA_X, COORDENADA_Y e ZONA de todos os blocos que os têm (postes,
        /// elétricos, blocos antigos). A geolocalização do AutoCAD, se houver e estiver noutra zona, passa para a do
        /// projeto. Usado depois de trocar a zona e pelo botão Atualizar Coordenadas da janela Configurações.
        /// </summary>
        internal static void UpdateCoordinates(Editor ed, Database db, UtmSettings utm)
        {
            int poles = 0, blocks = 0, locked = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                foreach (ObjectId id in modelSpace)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br) continue;

                    PoleData? pole = XDataTags.ReadPole(br);
                    bool hasCoordinates = br.AttributeCollection.Cast<ObjectId>()
                        .Any(a => CadHelpers.CoordinateAttribute(((AttributeReference)tr.GetObject(a, OpenMode.ForRead)).Tag.Trim(), br.Position, utm) != null);
                    if (pole == null && !hasCoordinates) continue;

                    try
                    {
                        if (pole != null)
                        {
                            var space = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite);
                            PoleLabels.Place(tr, db, space, br, pole);
                            poles++;
                        }
                        if (hasCoordinates)
                        {
                            CadHelpers.SetAttributes(tr, br, tag => CadHelpers.CoordinateAttribute(tag, br.Position, utm));
                            if (pole == null) blocks++;
                        }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.OnLockedLayer)
                    {
                        locked++;
                    }
                }
                tr.Commit();
            }

            string zone = $"zona {utm.Zone} {(utm.South ? "Sul" : "Norte")}";
            if (poles + blocks == 0) ed.WriteMessage($"\n[INFO]: Nenhum poste ou bloco com coordenadas no desenho ({zone}).");
            else ed.WriteMessage($"\n[SUCESSO]: Coordenadas atualizadas na {zone}: {poles} poste(s)" + (blocks > 0 ? $" e {blocks} outro(s) bloco(s)." : "."));
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} bloco(s) em layer travada ficaram com as coordenadas antigas.");

            switch (UtmZone.MoveGeoLocation(db, utm))
            {
                case string code: ed.WriteMessage($"\n[INFO]: Geolocalização do desenho passada para {code}."); break;
                case null when UtmZone.FromGeoLocation(db) is UtmSettings geo && (geo.Zone != utm.Zone || geo.South != utm.South):
                    ed.WriteMessage($"\n[AVISO]: A geolocalização do AutoCAD continua na zona {geo.Zone}; troque em GEOGRAPHICLOCATION.");
                    break;
            }
            ed.Regen();
        }
    }
}
