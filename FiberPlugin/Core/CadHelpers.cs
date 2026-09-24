using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Rotinas repetidas nos comandos: layers, inserção de blocos com atributos, textos, seleção de cabo e pontos.
    /// </summary>
    public static class CadHelpers
    {
        // Tags de atributo reconhecidas nos blocos
        public static readonly string[] NumberTags = { "NÚMERO", "NUMERO", "ID" };
        public static readonly string[] NameTags = { "NOME", "TIPO", "INFO", "DESCRICAO" };

        public static bool IsTag(string tag, string[] tags)
        {
            string t = tag.Trim();
            return tags.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase));
        }

        public static void EnsureLayer(Transaction tr, Database db, string name, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            lt.DowngradeOpen();
        }

        public static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(FiberSettings.AppName)) return;

            rat.UpgradeOpen();
            var ratr = new RegAppTableRecord { Name = FiberSettings.AppName };
            rat.Add(ratr);
            tr.AddNewlyCreatedDBObject(ratr, true);
            rat.DowngradeOpen();
        }

        /// <summary>Troca caracteres que não são aceitos em nomes de layer/bloco.</summary>
        public static string SanitizeName(string name)
        {
            char[] invalid = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };
            return new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        }

        /// <summary>Normaliza o ângulo para que o texto nunca fique de cabeça para baixo.</summary>
        public static double ReadableAngle(double angle)
        {
            angle = Math.IEEERemainder(angle, 2.0 * Math.PI); // [-π, π]
            if (angle > Math.PI / 2.0 + 0.001) angle -= Math.PI;
            else if (angle < -Math.PI / 2.0 - 0.001) angle += Math.PI;
            return angle;
        }

        /// <summary>Nome real do bloco (resolve blocos dinâmicos, que internamente viram *U...).</summary>
        public static string GetBlockName(Transaction tr, BlockReference br)
        {
            if (!br.IsDynamicBlock) return br.Name;
            return ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name;
        }

        /// <summary>Valor do primeiro atributo cuja tag está na lista (null se não houver).</summary>
        public static string? GetAttributeValue(Transaction tr, BlockReference br, string[] tags)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                if (IsTag(attRef.Tag, tags)) return attRef.TextString;
            }
            return null;
        }

        /// <summary>
        /// Insere uma referência de bloco criando os atributos. attributeValue recebe a TAG e
        /// devolve o texto a gravar (ou null para manter o valor padrão do bloco).
        /// </summary>
        public static BlockReference InsertBlock(Transaction tr, BlockTableRecord space, ObjectId blockId,
            Point3d position, double rotation, string? layer, Func<string, string?>? attributeValue = null)
        {
            var blockDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);

            var blockRef = new BlockReference(position, blockId)
            {
                Rotation = rotation,
                ScaleFactors = new Scale3d(1.0)
            };
            if (layer != null) blockRef.Layer = layer;

            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            if (blockDef.HasAttributeDefinitions)
            {
                foreach (ObjectId id in blockDef)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition attDef || attDef.Constant) continue;

                    var attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);

                    string? value = attributeValue?.Invoke(attDef.Tag.Trim());
                    if (value != null) attRef.TextString = value;

                    blockRef.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }

            return blockRef;
        }

        public static MText AddText(Transaction tr, BlockTableRecord space, Point3d location, string contents,
            double rotation, AttachmentPoint attachment, string layer)
        {
            var txt = new MText
            {
                Location = location,
                Contents = contents,
                TextHeight = FiberSettings.TextHeight,
                Rotation = rotation,
                Attachment = attachment,
                Layer = layer
            };
            space.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
            return txt;
        }

        /// <summary>Mostra a janela de escolha de cabo. Retorna null se o usuário cancelar.</summary>
        public static CableModel? SelectCable(Editor ed, string buttonText = "OK")
        {
            List<CableModel> cables = CableProvider.GetCables(ed);

            using (var form = new UI.CableSelectionForm(cables, buttonText))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return null;
                return form.SelectedCable;
            }
        }

        /// <summary>
        /// Pede uma sequência de pontos com linha elástica. Enter finaliza, Esc cancela (retorna null).
        /// </summary>
        public static List<Point3d>? GetPointSequence(Editor ed, string firstPrompt, string nextPrompt)
        {
            PromptPointResult first = ed.GetPoint(new PromptPointOptions(firstPrompt));
            if (first.Status != PromptStatus.OK) return null;

            var points = new List<Point3d> { first.Value };

            while (true)
            {
                var opts = new PromptPointOptions(nextPrompt)
                {
                    UseBasePoint = true,
                    BasePoint = points[points.Count - 1],
                    AllowNone = true
                };

                PromptPointResult res = ed.GetPoint(opts);
                if (res.Status == PromptStatus.Cancel) return null;
                if (res.Status == PromptStatus.None) break;
                if (res.Status == PromptStatus.OK) points.Add(res.Value);
            }

            return points;
        }
    }
}
