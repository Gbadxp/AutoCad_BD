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

        /// <summary>Layer que não imprime (quadro de folhas, bordas de viewport, marcações de verificação).</summary>
        public static void EnsureNonPlottingLayer(Transaction tr, Database db, string name, short colorIndex)
        {
            EnsureLayer(tr, db, name, colorIndex);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
            if (ltr.IsPlottable)
            {
                ltr.UpgradeOpen();
                ltr.IsPlottable = false;
            }
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
            return new string(name.Select(c => LayerStyle.InvalidNameChars.Contains(c) ? '-' : c).ToArray());
        }

        /// <summary>Cria a layer com a aparência dada; se ela já existir, fica como está (o usuário pode ter mudado).</summary>
        /// <returns>Aviso se o tipo de linha não existir no acadiso.lin (a layer fica contínua), ou null.</returns>
        public static string? EnsureLayer(Transaction tr, Database db, string name, LayerStyle style)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return null;
            EnsureLayer(tr, db, name, style.Color);
            return ApplyLayerStyle(tr, db, name, style);
        }

        /// <summary>Troca cor, tipo de linha e espessura de uma layer que já existe (nada se ela não existir).</summary>
        /// <returns>Aviso se o tipo de linha não existir no acadiso.lin (a layer fica contínua), ou null.</returns>
        public static string? ApplyLayerStyle(Transaction tr, Database db, string name, LayerStyle style)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name)) return null;

            string? warning = null;
            ObjectId linetype = db.ContinuousLinetype;
            if (style.Linetype is string wanted)
            {
                ObjectId loaded = LinetypeId(tr, db, wanted);
                if (loaded.IsNull) warning = $"Tipo de linha '{wanted}' não encontrado no {LayerStyle.LinetypeFile}: a layer {name} ficou contínua.";
                else linetype = loaded;
            }

            var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, style.Color);
            ltr.LinetypeObjectId = linetype;
            ltr.LineWeight = style.WeightMm is double mm ? (LineWeight)(int)Math.Round(mm * 100) : LineWeight.ByLineWeightDefault;
            return warning;
        }

        /// <summary>Tipo de linha do desenho, carregado do acadiso.lin se ainda não estiver. Null se não existir.</summary>
        private static ObjectId LinetypeId(Transaction tr, Database db, string name)
        {
            var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (!table.Has(name))
            {
                try
                {
                    db.LoadLineTypeFile(name, LayerStyle.LinetypeFile);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    return ObjectId.Null;
                }
            }
            return table.Has(name) ? table[name] : ObjectId.Null;
        }

        /// <summary>Normaliza o ângulo para que o texto nunca fique de cabeça para baixo.</summary>
        public static double ReadableAngle(double angle) => PlanarMath.ReadableAngle(angle);

        /// <summary>Nome real do bloco (resolve blocos dinâmicos, que internamente viram *U...).</summary>
        public static string GetBlockName(Transaction tr, BlockReference br)
        {
            if (!br.IsDynamicBlock) return br.Name;
            return ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name;
        }

        /// <summary>Adiciona a entidade nova ao espaço e à transação.</summary>
        public static T Append<T>(Transaction tr, BlockTableRecord space, T ent) where T : Entity
        {
            space.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
            return ent;
        }

        /// <summary>
        /// Chave reserva de um registro cuja entrada original está quebrada. Remover ou substituir a entrada quebrada
        /// abriria o objeto inexistente, e isso pode derrubar o AutoCAD; a reserva fica ao lado e é lida primeiro.
        /// </summary>
        private const string SpareSuffix = "_2";

        /// <summary>Dados gravados no dicionário do desenho (escala, zona UTM...). Null se não houver.</summary>
        public static TypedValue[]? ReadDrawingRecord(Database db, string key)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if ((OpenRecord(tr, nod, key + SpareSuffix, OpenMode.ForRead) ?? OpenRecord(tr, nod, key, OpenMode.ForRead)) is not Xrecord xrec)
                    return null;
                using (ResultBuffer? data = xrec.Data)
                {
                    return data?.AsArray();
                }
            }
        }

        /// <summary>
        /// Grava o registro no dicionário do desenho. Se a entrada da chave estiver quebrada (ou não for um Xrecord), ela
        /// não é tocada: o registro vai para a chave reserva. Se as duas estiverem ocupadas, não grava (o desenho fica
        /// como está, sem risco de cair).
        /// </summary>
        public static void WriteDrawingRecord(Transaction tr, Database db, string key, params TypedValue[] values)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            Xrecord? existing = OpenRecord(tr, nod, key + SpareSuffix, OpenMode.ForWrite) ?? OpenRecord(tr, nod, key, OpenMode.ForWrite);
            if (existing != null)
            {
                using (var data = new ResultBuffer(values)) existing.Data = data;
                return;
            }

            string target = nod.Contains(key) ? key + SpareSuffix : key;
            if (nod.Contains(target)) return;
            nod.UpgradeOpen();
            Xrecord xrec;
            using (var data = new ResultBuffer(values)) xrec = new Xrecord { Data = data };
            nod.SetAt(target, xrec);
            tr.AddNewlyCreatedDBObject(xrec, true);
        }

        /// <summary>
        /// Xrecord gravado na chave, ou null se não houver ou se a entrada estiver quebrada (ex.: desenho recuperado
        /// depois de um crash, com a entrada apontando para um objeto que não chegou a ser salvo). Só protege quando
        /// o AutoCAD recusa a abertura com erro (eInvalidObjectId); às vezes ele mesmo cai ao abrir a entrada.
        /// </summary>
        private static Xrecord? OpenRecord(Transaction tr, DBDictionary nod, string key, OpenMode mode)
        {
            if (!nod.Contains(key)) return null;
            ObjectId id = nod.GetAt(key);
            if (id.IsNull || !id.IsValid || id.IsErased) return null;
            try
            {
                return tr.GetObject(id, mode) as Xrecord;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
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

            Append(tr, space, blockRef);

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

        /// <summary>Troca o valor dos atributos do bloco: attributeValue recebe a TAG e devolve o texto novo (null = manter).</summary>
        public static void SetAttributes(Transaction tr, BlockReference br, Func<string, string?> attributeValue)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                string? value = attributeValue(att.Tag.Trim());
                if (value == null) continue;
                att.UpgradeOpen();
                att.TextString = value;
            }
        }

        /// <param name="height">Altura do texto; null = a padrão na escala do desenho.</param>
        public static MText AddText(Transaction tr, BlockTableRecord space, Point3d location, string contents,
            double rotation, AttachmentPoint attachment, string layer, double? height = null)
        {
            return Append(tr, space, new MText
            {
                Location = location,
                Contents = contents,
                TextHeight = height ?? DrawingScale.TextHeight(space.Database),
                Rotation = rotation,
                Attachment = attachment,
                Layer = layer
            });
        }

        /// <summary>Texto puro para o MText (barra invertida e chaves são códigos de formatação).</summary>
        public static string MTextLiteral(string text) => text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

        public static BlockTableRecord OpenModelSpace(Transaction tr, Database db, OpenMode mode)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], mode);
        }

        /// <summary>
        /// Valor dos atributos de coordenada preenchidos na inserção (COORDENADA_X / COORDENADA_Y / ZONA),
        /// no formato "405110.92 m E", "9032585.41 m S" e "20 L".
        /// Null para qualquer outra tag.
        /// </summary>
        public static string? CoordinateAttribute(string tag, Point3d point, UtmSettings? utm)
        {
            string t = tag.ToUpperInvariant().Replace(' ', '_');
            if (t == "COORDENADA_X") return UtmZone.EastingText(point.X);
            if (t == "COORDENADA_Y") return UtmZone.NorthingText(point.Y, utm?.South ?? true);
            if (utm != null && (t == "ZONA" || t == "FUSO" || t == "ZONA_UTM")) return UtmZone.ZoneText(point, utm);
            return null;
        }

        /// <summary>Pergunta [Sim/Nao] com Sim como padrão (Enter = Sim).</summary>
        public static bool AskYes(Editor ed, string message) => AskKeyword(ed, message, "Sim Nao", "Sim") == "Sim";

        /// <summary>Pergunta uma opção; Enter escolhe <paramref name="defaultKeyword"/>. Null se o usuário cancelar.</summary>
        public static string? AskKeyword(Editor ed, string message, string keywords, string defaultKeyword)
        {
            PromptResult res = ed.GetKeywords(new PromptKeywordOptions(message, keywords) { AllowNone = true });
            if (res.Status == PromptStatus.None) return defaultKeyword;
            return res.Status == PromptStatus.OK ? res.StringResult : null;
        }

        /// <summary>Pede um inteiro; Enter aceita <paramref name="defaultValue"/> (se houver). Null se o usuário cancelar.</summary>
        public static int? AskInt(Editor ed, string message, int? defaultValue, int min = 1, int max = int.MaxValue)
        {
            var pio = new PromptIntegerOptions(message)
            {
                AllowNone = defaultValue != null,
                AllowNegative = min < 0,
                AllowZero = min <= 0,
                LowerLimit = min,
                UpperLimit = max
            };
            PromptIntegerResult res = ed.GetInteger(pio);
            if (res.Status == PromptStatus.OK) return res.Value;
            return res.Status == PromptStatus.None ? defaultValue : null;
        }

        /// <summary>Pergunta qual arquivo abrir. Null se o usuário cancelar.</summary>
        public static string? AskOpenPath(string title, string filter)
        {
            using (var ofd = new System.Windows.Forms.OpenFileDialog())
            {
                ofd.Filter = filter;
                ofd.Title = title;
                return ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK ? ofd.FileName : null;
            }
        }

        /// <summary>Pergunta onde salvar um arquivo. Null se o usuário cancelar.</summary>
        public static string? AskSavePath(string title, string defaultFileName, string filter)
        {
            using (var sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Filter = filter;
                sfd.Title = title;
                sfd.FileName = defaultFileName;
                return sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK ? sfd.FileName : null;
            }
        }

        /// <summary>Mostra a janela de escolha de cabo. Retorna null se o usuário cancelar.</summary>
        public static CableModel? SelectCable(Editor ed, string buttonText = "OK")
        {
            List<CableModel> cables = CableProvider.GetCables(ed);
            if (cables.Count == 0) return null; // GetCables já explicou o motivo no Editor

            using (var form = UI.Pickers.Cable(cables, buttonText))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return null;
                return form.Selected;
            }
        }

        /// <summary>
        /// Pede uma sequência de pontos como o comando LINE: linha elástica a partir do último ponto e o
        /// caminho já clicado aparecendo na tela (na cor <paramref name="previewColor"/>) a cada clique.
        /// "Desfazer" remove o último ponto, Enter finaliza e Esc cancela (retorna null).
        /// Os pontos retornados estão em coordenadas do mundo (WCS).
        /// </summary>
        public static List<Point3d>? GetPointSequence(Editor ed, string firstPrompt, string nextPrompt, short previewColor = 3)
        {
            PromptPointResult first = ed.GetPoint(new PromptPointOptions(firstPrompt));
            if (first.Status != PromptStatus.OK) return null;

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            var picked = new List<Point3d> { first.Value };   // UCS: base da linha elástica
            var points = new List<Point3d> { first.Value.TransformBy(ucs) };

            // "Selecione o próximo ponto (...):" → "Selecione o próximo ponto (...) [Desfazer]: "
            string message = nextPrompt.TrimEnd().TrimEnd(':').TrimEnd() + " [Desfazer]: ";

            using (var preview = new PathPreview(previewColor))
            {
                while (true)
                {
                    var opts = new PromptPointOptions(message, "Desfazer")
                    {
                        UseBasePoint = true,
                        BasePoint = picked[picked.Count - 1],
                        AllowNone = true
                    };

                    PromptPointResult res = ed.GetPoint(opts);
                    if (res.Status == PromptStatus.Cancel) return null;
                    if (res.Status == PromptStatus.None) break;

                    if (res.Status == PromptStatus.Keyword)
                    {
                        if (picked.Count > 1)
                        {
                            picked.RemoveAt(picked.Count - 1);
                            points.RemoveAt(points.Count - 1);
                        }
                        else
                        {
                            ed.WriteMessage("\nNada para desfazer.");
                        }
                    }
                    else if (res.Status == PromptStatus.OK)
                    {
                        picked.Add(res.Value);
                        points.Add(res.Value.TransformBy(ucs));
                    }

                    preview.Update(points);
                }
            }

            return points;
        }
    }
}
