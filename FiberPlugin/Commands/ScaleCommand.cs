using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class ScaleCommand
    {
        /// <summary>
        /// Define a escala do desenho. Textos dos vãos, pontos e setas de esforço passam a ser criados no
        /// tamanho certo para essa escala, e as anotações já desenhadas podem ser ajustadas na hora.
        /// </summary>
        [CommandMethod("FIBRA_ESCALA")]
        public void SetScale()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int current = DrawingScale.Get(db);

            var pio = new PromptIntegerOptions($"\nEscala do desenho 1:X (ex.: 500, 1000, 2000) <{current}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = DrawingScale.Min,
                UpperLimit = DrawingScale.Max
            };
            PromptIntegerResult pir = ed.GetInteger(pio);
            if (pir.Status == PromptStatus.Cancel) return;
            int scale = pir.Status == PromptStatus.OK ? pir.Value : current;

            if (scale == current)
            {
                ed.WriteMessage($"\n[INFO]: Escala mantida em 1:{current}.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DrawingScale.Set(tr, db, scale);

                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                Annotations existing = FindAnnotations(tr, modelSpace);

                int adjusted = 0;
                if (existing.Count > 0 && AskYes(ed, $"\nAjustar as {existing.Count} anotações já desenhadas para 1:{scale}? [Sim/Nao] <Sim>: "))
                {
                    adjusted = Rescale(tr, existing, (double)scale / current);
                }

                tr.Commit();

                ed.WriteMessage($"\n[SUCESSO]: Escala do desenho: 1:{scale} (texto de {DrawingScale.TextHeight(db):0.##} unidades).");
                if (adjusted > 0) ed.WriteMessage($"\n[INFO]: {adjusted} anotação(ões) ajustada(s).");
            }
            ed.Regen();
        }

        private class Annotations
        {
            public List<(ObjectId Id, Point3d Pole)> EffortMarkers { get; } = new List<(ObjectId, Point3d)>();
            public List<ObjectId> SpanLabels { get; } = new List<ObjectId>();
            public List<ObjectId> PointLabels { get; } = new List<ObjectId>();
            public int Count => EffortMarkers.Count + SpanLabels.Count + PointLabels.Count;
        }

        /// <summary>Anotações criadas pelo plugin: setas de esforço, textos dos vãos e textos dos pontos.</summary>
        private static Annotations FindAnnotations(Transaction tr, BlockTableRecord space)
        {
            var found = new Annotations();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

                if (XDataTags.TryGetEffortPole(ent, out Point3d pole))
                {
                    found.EffortMarkers.Add((id, pole));
                }
                else if (ent is MText txt)
                {
                    if (txt.Layer.StartsWith(FiberSettings.CableLayerPrefix, StringComparison.OrdinalIgnoreCase))
                        found.SpanLabels.Add(id);
                    else if (txt.Layer.Equals(FiberSettings.CoordinatesLayer, StringComparison.OrdinalIgnoreCase) ||
                             txt.Layer.Equals(PoleLabels.Layer, StringComparison.OrdinalIgnoreCase))
                        found.PointLabels.Add(id);
                }
            }
            return found;
        }

        private static int Rescale(Transaction tr, Annotations annotations, double ratio)
        {
            // Setas de esforço (seta + textos, ou bloco): escala em torno do centro do poste
            foreach (var (id, pole) in annotations.EffortMarkers)
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                ent.TransformBy(Matrix3d.Scaling(ratio, pole));
            }

            // Textos dos vãos: altura nova e afastamento da linha proporcional, sem mudar de vão
            foreach (ObjectId id in annotations.SpanLabels)
            {
                var txt = (MText)tr.GetObject(id, OpenMode.ForWrite);
                double extraGap = txt.TextHeight * (FiberSettings.LabelGap / FiberSettings.TextHeight) * (ratio - 1);
                var up = new Vector3d(-Math.Sin(txt.Rotation), Math.Cos(txt.Rotation), 0);

                if (txt.Attachment == AttachmentPoint.BottomCenter) txt.Location += up * extraGap;
                else if (txt.Attachment == AttachmentPoint.TopCenter) txt.Location -= up * extraGap;

                txt.TextHeight *= ratio;
            }

            // Textos dos pontos (P01...) e dos postes: só a altura
            foreach (ObjectId id in annotations.PointLabels)
            {
                var txt = (MText)tr.GetObject(id, OpenMode.ForWrite);
                txt.TextHeight *= ratio;
            }

            return annotations.Count;
        }

        private static bool AskYes(Editor ed, string message)
        {
            var pko = new PromptKeywordOptions(message, "Sim Nao") { AllowNone = true };
            PromptResult res = ed.GetKeywords(pko);
            return res.Status == PromptStatus.None || (res.Status == PromptStatus.OK && res.StringResult == "Sim");
        }
    }
}
