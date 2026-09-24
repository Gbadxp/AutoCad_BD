using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class DrawCableCommand
    {
        [CommandMethod("FIBRA_LANCAR_CABO")]
        public void DrawCable()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            CableModel? cable = CadHelpers.SelectCable(ed, "Lançar");
            if (cable == null) return;

            List<Point3d>? points = CadHelpers.GetPointSequence(ed,
                "\nSelecione o ponto inicial do cabo: ",
                "\nSelecione o próximo ponto (ou pressione Enter para finalizar): ");
            if (points == null) return;

            if (points.Count < 2)
            {
                ed.WriteMessage("\n[AVISO]: É necessário pelo menos dois pontos para lançar um cabo.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var spans = Enumerable.Range(0, points.Count - 1).Select(i => points[i].DistanceTo(points[i + 1])).ToList();
                Polyline poly = CableDrawing.Draw(tr, db, space, points, spans, cable);

                ed.WriteMessage($"\n[AVISO]: Cabo '{cable.FullName}' lançado com sucesso!");
                ed.WriteMessage($"\n[COMPRIMENTO TOTAL]: {poly.Length:F2} metros.");

                tr.Commit();
            }
            ed.UpdateScreen();
        }
    }
}
