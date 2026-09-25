using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            Point3d position, double rotation, string? layer, Func<string, string?>? attributeValue = null, double scale = 1.0)
        {
            var blockDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);

            var blockRef = new BlockReference(position, blockId)
            {
                Rotation = rotation,
                ScaleFactors = new Scale3d(scale)
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
                TextHeight = DrawingScale.TextHeight(space.Database),
                Rotation = rotation,
                Attachment = attachment,
                Layer = layer
            };
            space.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
            return txt;
        }

        public static BlockTableRecord OpenModelSpace(Transaction tr, Database db, OpenMode mode)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], mode);
        }

        /// <summary>
        /// Valor dos atributos de coordenada preenchidos na inserção (COORDENADA_X / COORDENADA_Y).
        /// Null para qualquer outra tag.
        /// </summary>
        public static string? CoordinateAttribute(string tag, Point3d point)
        {
            string t = tag.ToUpperInvariant();
            if (t == "COORDENADA_X" || t == "COORDENADA X") return point.X.ToString("F2", CultureInfo.InvariantCulture) + " m E";
            if (t == "COORDENADA_Y" || t == "COORDENADA Y") return point.Y.ToString("F2", CultureInfo.InvariantCulture) + " m S";
            return null;
        }

        /// <summary>Metragem total de cada tipo de cabo desenhado (chave = nome curto do cabo).</summary>
        public static Dictionary<string, double> CableLengths(Transaction tr, BlockTableRecord space)
        {
            var lengths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline poly) continue;

                string? cableName = XDataTags.GetCableName(poly);
                if (cableName == null) continue;

                lengths[cableName] = lengths.TryGetValue(cableName, out double len) ? len + poly.Length : poly.Length;
            }
            return lengths;
        }

        /// <summary>
        /// Pergunta onde salvar e grava o CSV (UTF-8, abre direto no Excel). Mostra no Editor o
        /// resultado; retorna false se o usuário cancelar ou der erro.
        /// </summary>
        public static bool SaveCsv(Editor ed, string title, string defaultFileName, Action<StreamWriter> write)
        {
            using (var sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Filter = "Comma Separated Values (*.csv)|*.csv|All files (*.*)|*.*";
                sfd.Title = title;
                sfd.FileName = defaultFileName;

                if (sfd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    ed.WriteMessage("\n[AVISO]: Exportação cancelada pelo usuário.");
                    return false;
                }

                try
                {
                    using (var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        write(sw);
                    }
                    ed.WriteMessage($"\n[SUCESSO]: Arquivo salvo em: {sfd.FileName}");
                    return true;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[ERRO]: Não foi possível salvar o arquivo. Detalhes: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>Mostra a janela de escolha de cabo. Retorna null se o usuário cancelar.</summary>
        public static CableModel? SelectCable(Editor ed, string buttonText = "OK")
        {
            List<CableModel> cables = CableProvider.GetCables(ed);
            if (cables.Count == 0) return null; // GetCables já explicou o motivo no Editor

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
