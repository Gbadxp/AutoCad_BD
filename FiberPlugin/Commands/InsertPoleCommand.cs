using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class InsertPoleCommand
    {
        // Último modelo usado, já selecionado na próxima vez (durante a sessão do AutoCAD)
        private static PoleData? _lastModel;

        /// <summary>
        /// Insere postes do modelo escolhido na lista (Configurações > Postes): bloco DT ou CC do BLOCOS.dwg,
        /// já identificado: número em sequência, texto com número,
        /// altura/esforço e coordenada UTM, e o tipo DT/CC gravado para a listagem de postes.
        /// </summary>
        [CommandMethod("FIBRA_INSERIR_POSTE")]
        public void InsertPole()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            List<PoleData> models = PoleModels.Load(ed);
            if (models.Count == 0) return;

            PoleData? model = ChooseModel(models);
            if (model == null) return;

            ObjectId blockId = LoadBlock(ed, db, model.Type);
            if (blockId.IsNull) return;

            // Zona UTM do texto do poste (a linha "20 L"): desenho novo, sem ela, recebe a do projeto
            UtmSettings? utm = UtmZone.Ensure(ed, db, out bool zoneAssigned);
            if (utm == null)
            {
                ed.WriteMessage("\n[DICA]: Zona UTM do desenho não definida. Defina em Configurações > Projeto para incluir a zona (ex.: 20 L) nos textos.");
            }

            HashSet<int> used;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                used = Poles.UsedNumbers(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                tr.Commit();
            }
            // Os postes que já estavam no desenho, com o texto sem a zona, ganham a linha da zona também
            if (zoneAssigned && used.Count > 0) UtmZoneCommand.UpdateCoordinates(ed, db, utm!);

            int? start = Poles.AskNumber(ed, Poles.NextFree(used, 1));
            if (start == null) return;
            int number = start.Value;
            int inserted = 0;

            while (true)
            {
                number = Poles.NextFree(used, number);

                var ppo = new PromptPointOptions(
                    $"\nPoste {PoleData.NumberText(number)} ({model.Designation}): ponto de inserção [Modelo/Numero]: ",
                    "Modelo Numero")
                {
                    AllowNone = true
                };

                PromptPointResult res = ed.GetPoint(ppo);
                if (res.Status == PromptStatus.Cancel || res.Status == PromptStatus.None) break;

                if (res.Status == PromptStatus.Keyword)
                {
                    if (res.StringResult == "Modelo")
                    {
                        PoleData? other = ChooseModel(models);
                        if (other != null)
                        {
                            ObjectId otherBlock = LoadBlock(ed, db, other.Type);
                            if (!otherBlock.IsNull)
                            {
                                model = other;
                                blockId = otherBlock;
                            }
                        }
                    }
                    else if (res.StringResult == "Numero")
                    {
                        int? n = Poles.AskNumber(ed, number);
                        if (n != null) number = n.Value;
                    }
                    continue;
                }
                if (res.Status != PromptStatus.OK) continue;

                Point3d point = res.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                var data = new PoleData { Number = number, Type = model.Type, HeightM = model.HeightM, EffortDaN = model.EffortDaN };

                // Poste DT (Duplo T) tem orientação: gira com o mouse. O CC é circular e não precisa.
                double rotation = 0;
                if (model.Type == PoleData.DoubleT)
                {
                    double? angle = BlockInsertHelpers.DragRotation(ed, blockId, point,
                        "\nGire o poste com o mouse e clique (ou digite o ângulo) <0>: ", PoleScale(db, blockId));
                    if (angle == null) break;
                    rotation = angle.Value;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PlacePole(tr, db, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite), blockId, point, rotation, data, utm);
                    tr.Commit();
                }

                used.Add(number);
                inserted++;
                ed.WriteMessage($"\n[OK]: {PoleData.NumberText(number)} | {data.Designation} | E {point.X:F2} N {point.Y:F2}");
            }

            if (inserted > 0) ed.WriteMessage($"\n[INFO]: {inserted} poste(s) inserido(s).");
        }

        /// <summary>Poste completo: bloco com atributos, dados gravados (XData) e texto de identificação.</summary>
        internal static void PlacePole(Transaction tr, Database db, BlockTableRecord space, ObjectId blockId, Point3d point,
            double rotation, PoleData data, UtmSettings? utm)
        {
            BlockReference br = CadHelpers.InsertBlock(tr, space, blockId, point, rotation, null, tag =>
            {
                if (CadHelpers.IsTag(tag, CadHelpers.NumberTags)) return PoleData.NumberText(data.Number);
                if (CadHelpers.IsTag(tag, CadHelpers.NameTags)) return data.HeightEffort;
                return CadHelpers.CoordinateAttribute(tag, point, utm);
            }, PoleScale(db, blockId));

            XDataTags.TagPole(tr, db, br, data);
            PoleLabels.Place(tr, db, space, br, data, PoleLabels.NewBlock);
        }

        /// <summary>
        /// Troca o modelo de postes já inseridos (ex.: DT 9/200 lançado no lugar de DT 11/300, ou DT por CC): escolhe o
        /// modelo novo na lista e clica nos postes, um por vez, até o Enter (postes já selecionados antes são trocados
        /// direto). O poste fica no lugar, com o número, o esforço existente e o ID da Energisa.
        /// </summary>
        [CommandMethod("FIBRA_TROCAR_POSTE", CommandFlags.UsePickSet)]
        public void ChangePole()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (!CadHelpers.InModelSpace(ed, db)) return;

            // Postes já selecionados antes do comando são trocados direto (o resto da seleção não conta)
            PromptSelectionResult implied = ed.SelectImplied();
            var preselected = new List<ObjectId>();
            if (implied.Status == PromptStatus.OK)
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    preselected = implied.Value.GetObjectIds()
                        .Where(id => tr.GetObject(id, OpenMode.ForRead) is BlockReference b && XDataTags.ReadPole(b) != null).ToList();
                }
            }

            List<PoleData> models = PoleModels.Load(ed);
            if (models.Count == 0) return;
            PoleData? model = ChooseModel(models);
            if (model == null) return;
            ObjectId blockId = LoadBlock(ed, db, model.Type);
            if (blockId.IsNull) return;
            UtmSettings? utm = UtmZone.Get(db);

            int changed = 0;
            bool heightChanged = false, nowDoubleT = false;
            foreach (ObjectId id in preselected) Change(id);
            if (preselected.Count == 0)
            {
                while (true)
                {
                    var options = new PromptEntityOptions($"\nClique no poste a trocar para {model.Designation} (ou Enter para terminar): ")
                    {
                        AllowNone = true
                    };
                    options.SetRejectMessage("\nIsso não é um bloco de poste.");
                    options.AddAllowedClass(typeof(BlockReference), true);
                    PromptEntityResult picked = ed.GetEntity(options);
                    if (picked.Status != PromptStatus.OK) break;
                    Change(picked.ObjectId);
                }
            }
            if (changed == 0) return;
            if (changed > 1) ed.WriteMessage($"\n[SUCESSO]: {changed} postes trocados para {model.Designation}.");
            if (heightChanged)
                ed.WriteMessage("\n[AVISO]: A altura do poste mudou: rode o Esforço no Percurso de novo (a seta mostra o esforço a 20 cm do " +
                                "topo, que depende da altura).");
            if (nowDoubleT)
                ed.WriteMessage("\n[DICA]: Poste que era CC virou DT na mesma rotação: se precisar, gire o bloco (ROTATE) para as faces ficarem " +
                                "do lado do cabo.");

            void Change(ObjectId id)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br || XDataTags.ReadPole(br) is not PoleData old)
                    {
                        ed.WriteMessage("\n[AVISO]: Isso não é um poste do plugin (inserido pelo Inserir Postes).");
                        return;
                    }
                    string name = PoleData.NumberText(old.Number);
                    if (old.SameModel(model))
                    {
                        ed.WriteMessage($"\n[INFO]: O {name} já é {model.Designation}.");
                        return;
                    }
                    if (CadHelpers.IsOnLockedLayer(tr, id) || br.AttributeCollection.Cast<ObjectId>().Any(a => CadHelpers.IsOnLockedLayer(tr, a)))
                    {
                        ed.WriteMessage($"\n[AVISO]: O {name} está em layer travada: destrave para trocar o poste.");
                        return;
                    }

                    try
                    {
                        ChangeModel(tr, db, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForWrite), br, old, model, blockId, utm);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.OnLockedLayer)
                    {
                        // Texto do poste em layer travada: nada muda (a transação é desfeita)
                        ed.WriteMessage($"\n[AVISO]: O texto do {name} está em layer travada: destrave para trocar o poste.");
                        return;
                    }
                    tr.Commit();
                    changed++;
                    heightChanged |= Math.Abs(old.HeightM - model.HeightM) > 1e-9;
                    nowDoubleT |= old.Type == PoleData.Circular && model.Type == PoleData.DoubleT;
                    ed.WriteMessage($"\n[INFO]: {name}: {old.Designation} trocado para {model.Designation}.");
                }
            }
        }

        /// <summary>
        /// Poste com o modelo novo, no mesmo objeto: com o mesmo handle, setas de esforço, CTO/CEO e texto continuam ligados
        /// a ele. De DT para CC (ou o contrário) troca o desenho do bloco e refaz os atributos pelo bloco novo (como o
        /// ATTSYNC, mantendo os valores); depois atributos, dados gravados (XData) e texto de identificação com o modelo
        /// novo. Número, esforço existente e ID da Energisa ficam.
        /// </summary>
        internal static void ChangeModel(Transaction tr, Database db, BlockTableRecord space, BlockReference br, PoleData old,
            PoleData model, ObjectId blockId, UtmSettings? utm)
        {
            var data = new PoleData
            {
                Number = old.Number, Type = model.Type, HeightM = model.HeightM, EffortDaN = model.EffortDaN,
                ExistingKgf = old.ExistingKgf, EnergisaId = old.EnergisaId
            };
            br.UpgradeOpen();
            if (!string.Equals(old.Type, model.Type, StringComparison.OrdinalIgnoreCase))
            {
                // O bloco novo fica no tamanho padrão dele, na mesma proporção em que o de antes estava do dele (poste que
                // foi aumentado à mão continua maior)
                double ratio = Math.Abs(br.ScaleFactors.X) / PoleScale(db, br.BlockTableRecord);
                br.BlockTableRecord = blockId;
                br.ScaleFactors = new Scale3d(PoleScale(db, blockId) * ratio);
                SyncAttributes(tr, br);
            }
            CadHelpers.SetAttributes(tr, br, tag =>
            {
                if (CadHelpers.IsTag(tag, CadHelpers.NumberTags)) return PoleData.NumberText(data.Number);
                if (CadHelpers.IsTag(tag, CadHelpers.NameTags)) return data.HeightEffort;
                return CadHelpers.CoordinateAttribute(tag, br.Position, utm);
            });
            XDataTags.TagPole(tr, db, br, data);
            PoleLabels.Place(tr, db, space, br, data);
        }

        /// <summary>
        /// Atributos do bloco iguais aos do desenho dele (depois de trocar o desenho): cada atributo vai para o lugar e o
        /// estilo do bloco novo com o valor que tinha, os que faltam são criados e os que o bloco novo não tem saem.
        /// </summary>
        private static void SyncAttributes(Transaction tr, BlockReference br)
        {
            var definition = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            List<AttributeReference> current = br.AttributeCollection.Cast<ObjectId>()
                .Select(id => (AttributeReference)tr.GetObject(id, OpenMode.ForWrite)).ToList();
            var kept = new HashSet<ObjectId>();
            foreach (ObjectId id in definition)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition attDef || attDef.Constant) continue;
                AttributeReference? att = current.FirstOrDefault(a => !kept.Contains(a.ObjectId) &&
                    a.Tag.Trim().Equals(attDef.Tag.Trim(), StringComparison.OrdinalIgnoreCase));
                string? value = att?.TextString;
                if (att == null)
                {
                    att = new AttributeReference();
                    att.SetAttributeFromBlock(attDef, br.BlockTransform);
                    br.AttributeCollection.AppendAttribute(att);
                    tr.AddNewlyCreatedDBObject(att, true);
                }
                else
                {
                    att.SetAttributeFromBlock(attDef, br.BlockTransform);
                }
                if (value != null) att.TextString = value;
                kept.Add(att.ObjectId);
            }
            foreach (AttributeReference att in current.Where(a => !kept.Contains(a.ObjectId))) att.Erase();
        }

        /// <summary>
        /// Escala do bloco do poste: o tamanho padrão das Configurações (Desenho > Tamanho dos blocos) ou, sem ele, o do
        /// BLOCOS.dwg, na escala do ícone dos postes.
        /// </summary>
        internal static double PoleScale(Database db, ObjectId blockId)
        {
            double factor = DrawingScale.Factor(db, ScaleItem.PoleIcon);
            return BlockInsertHelpers.SymbolScale(db, blockId, UserSettings.Current.PoleSymbolSize, factor, factor);
        }

        internal static PoleData? ChooseModel(List<PoleData> models)
        {
            using (var form = UI.Pickers.Pole(models, _lastModel))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return null;
                _lastModel = form.Selected;
                return form.Selected;
            }
        }

        /// <summary>Bloco DT ou CC do BLOCOS.dwg, já copiado para o desenho. ObjectId.Null se não houver.</summary>
        internal static ObjectId LoadBlock(Editor ed, Database db, string type)
        {
            List<string> names = BlockCategories.Blocks(BlockCategories.Poles).Select(b => b.Name).ToList();
            string? blockName = PoleModels.BlockFor(type, names);

            if (blockName == null)
            {
                string typeName = type == PoleData.Circular ? "CC (Circular)" : "DT (Duplo T)";
                ed.WriteMessage($"\n[ERRO]: Nenhum bloco de poste {typeName} no BLOCOS.dwg. " +
                                $"Nomeie o bloco como \"{type}\" (ou \"POSTE {type}\").");
                if (BlockRepository.LastError != null) ed.WriteMessage($"\n[ERRO]: {BlockRepository.LastError}");
                return ObjectId.Null;
            }

            return BlockInsertHelpers.LoadBlock(ed, db, blockName);
        }
    }
}
