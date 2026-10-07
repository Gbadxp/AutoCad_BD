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

            List<PoleData> models = PoleModels.Load(ed);
            if (models.Count == 0) return;

            PoleData? model = ChooseModel(models);
            if (model == null) return;

            ObjectId blockId = LoadBlock(ed, db, model.Type);
            if (blockId.IsNull) return;

            // Zona UTM definida em Configurações > Projeto (sem ela, o texto sai sem a linha "20 L")
            UtmSettings? utm = UtmZone.Get(db);
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
                        "\nGire o poste com o mouse e clique (ou digite o ângulo) <0>: ", DrawingScale.Factor(db, ScaleItem.PoleIcon));
                    if (angle == null) break;
                    rotation = angle.Value;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PlacePole(tr, db, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite), blockId, point, rotation, data, utm);
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
            // Tamanho: escala do ícone dos postes (sem escala própria, o tamanho do BLOCOS.dwg)
            BlockReference br = CadHelpers.InsertBlock(tr, space, blockId, point, rotation, null, tag =>
            {
                if (CadHelpers.IsTag(tag, CadHelpers.NumberTags)) return PoleData.NumberText(data.Number);
                if (CadHelpers.IsTag(tag, CadHelpers.NameTags)) return data.HeightEffort;
                return CadHelpers.CoordinateAttribute(tag, point, utm);
            }, DrawingScale.Factor(db, ScaleItem.PoleIcon));

            XDataTags.TagPole(tr, db, br, data);
            PoleLabels.Place(tr, db, space, br, data);
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
