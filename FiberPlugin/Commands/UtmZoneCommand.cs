using System.Collections.Generic;
using System.Linq;
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
        /// Define a zona UTM e o hemisfério do projeto (gravados no DWG) e atualiza os textos dos postes,
        /// que também passam a mostrar a coordenada da posição atual de cada bloco.
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

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<PoleInfo> poles = Poles.Collect(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead))
                    .Where(p => p.Data != null)
                    .ToList();

                if (poles.Count > 0 &&
                    CadHelpers.AskYes(ed, $"\nAtualizar as coordenadas de {poles.Count} poste(s)? [Sim/Nao] <Sim>: "))
                {
                    foreach (PoleInfo pole in poles)
                    {
                        var br = (BlockReference)tr.GetObject(pole.Id, OpenMode.ForRead);
                        var space = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite);

                        PoleLabels.Place(tr, db, space, br, pole.Data!);
                        UpdateCoordinateAttributes(tr, br, utm);
                    }
                    ed.WriteMessage($"\n[INFO]: {poles.Count} poste(s) atualizado(s).");
                }

                tr.Commit();
            }
            ed.Regen();
        }

        private static void UpdateCoordinateAttributes(Transaction tr, BlockReference br, UtmSettings utm)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                string? value = CadHelpers.CoordinateAttribute(att.Tag, br.Position, utm);
                if (value == null) continue;

                att.UpgradeOpen();
                att.TextString = value;
            }
        }
    }
}
