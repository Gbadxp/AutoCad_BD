using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace FiberPlugin.Commands
{
    public class SettingsCommand
    {
        /// <summary>
        /// Janela Configurações: cabos (com a cor de cada um) e modelos de poste das planilhas da pasta Dados, prefixos dos
        /// nomes, tamanhos e cores das layers (UserSettings). Depois de salvar, leva as mudanças para o desenho aberto:
        /// cor das layers que já existem e, se o usuário confirmar, os nomes e a altura dos textos já desenhados.
        /// </summary>
        [CommandMethod("FIBRA_CONFIGURACOES")]
        public void EditSettings()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            UserSettings.Reload();
            UserSettings before = UserSettings.Current.Clone();

            var cableWarnings = new List<string>();
            List<CableModel> cablesBefore = CableProvider.Read(cableWarnings, out string? cableError);
            var poleWarnings = new List<string>();
            List<PoleData> polesBefore = PoleModels.Read(poleWarnings, out string? poleError);
            string originalCables = CableProvider.ToCsv(cablesBefore);
            string originalPoles = PoleModels.ToCsv(polesBefore);

            UserSettings after;
            List<CableModel> cablesAfter;
            using (var form = new UI.SettingsForm(cablesBefore, polesBefore, before,
                       Notes(CableProvider.FileName, cableError, cableWarnings), Notes(PoleModels.FileName, poleError, poleWarnings)))
            {
                // Só regrava a planilha que mudou: as outras ficam como o usuário deixou no Excel
                form.SaveChanges = f =>
                {
                    if (CableProvider.ToCsv(f.Cables) != originalCables && CableProvider.Save(f.Cables) is string e1) return e1;
                    if (PoleModels.ToCsv(f.PoleTypes) != originalPoles && PoleModels.Save(f.PoleTypes) is string e2) return e2;
                    return f.Settings.Save() is string e3 ? $"{UserSettings.FilePath}: {e3}" : null;
                };
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                after = form.Settings;
                cablesAfter = form.Cables;
            }

            ed.WriteMessage($"\n[SUCESSO]: Configurações salvas ({cablesAfter.Count} cabo(s), nomes {after.Name(after.PolePrefix, 1)}, " +
                            $"{after.Name(after.CtoPrefix, 1)}, {after.Name(after.CeoPrefix, 1)}, texto de {after.TextHeight:0.##} mm). " +
                            "Valem para todos os desenhos.");

            UpdateLayerColors(ed, db, LayerColorChanges(cablesBefore, cablesAfter, before, after));

            bool poleNames = before.PolePrefix != after.PolePrefix || before.NumberDigits != after.NumberDigits;
            bool boxNames = before.CtoPrefix != after.CtoPrefix || before.CeoPrefix != after.CeoPrefix || before.NumberDigits != after.NumberDigits;
            if (poleNames || boxNames) UpdateNames(ed, db, poleNames, boxNames);

            if (Math.Abs(after.TextHeight - before.TextHeight) > 1e-9) ScaleCommand.ResizeTexts(ed, db, after.TextHeight / before.TextHeight);
        }

        /// <summary>Aviso mostrado na aba da planilha (null se ela foi lida sem problemas).</summary>
        private static string? Notes(string fileName, string? error, List<string> warnings)
        {
            if (error != null) return error + " Ao salvar, a planilha é criada com o que estiver na tabela.";
            if (warnings.Count == 0) return null;
            return $"{fileName}: {warnings.Count} linha(s) ignorada(s) na leitura ({warnings[0]}). " +
                   "Se você mudar esta tabela, elas saem da planilha ao salvar.";
        }

        /// <summary>Layers do plugin cuja cor mudou: a de cada cabo (cor própria ou a padrão) e as dos textos e setas.</summary>
        private static Dictionary<string, short> LayerColorChanges(List<CableModel> cablesBefore, List<CableModel> cablesAfter,
            UserSettings before, UserSettings after)
        {
            var changes = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            foreach (CableModel cable in cablesAfter)
            {
                CableModel? old = CableProvider.Find(cablesBefore, cable.ShortName);
                short color = cable.Color ?? after.CableColor;
                if (old == null || (old.Color ?? before.CableColor) != color) changes[CableDrawing.LayerFor(cable)] = color;
            }
            if (before.PoleLabelColor != after.PoleLabelColor) changes[PoleLabels.Layer] = after.PoleLabelColor;
            if (before.BoxLabelColor != after.BoxLabelColor) changes[PoleLabels.BoxLayer] = after.BoxLabelColor;
            if (before.EffortColor != after.EffortColor) changes[FiberSettings.EffortLayer] = after.EffortColor;
            return changes;
        }

        /// <summary>Troca a cor das layers que já existem no desenho (as que não existem nascem com a cor nova).</summary>
        private static void UpdateLayerColors(Editor ed, Database db, Dictionary<string, short> changes)
        {
            if (changes.Count == 0) return;
            int changed = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (var (name, color) in changes.Select(c => (c.Key, c.Value)))
                {
                    if (!layers.Has(name)) continue;
                    var layer = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForWrite);
                    layer.Color = AcColor.FromColorIndex(AcColorMethod.ByAci, color);
                    changed++;
                }
                tr.Commit();
            }
            if (changed > 0)
            {
                ed.WriteMessage($"\n[INFO]: Cor nova em {changed} layer(s) deste desenho.");
                ed.Regen();
            }
        }

        /// <summary>
        /// Prefixo ou dígitos novos: pergunta se reescreve os nomes já desenhados (atributo de número do bloco e texto ao
        /// lado) dos postes e/ou das CTO/CEO inseridos pelo plugin. Os números não mudam.
        /// </summary>
        private static void UpdateNames(Editor ed, Database db, bool poles, bool boxes)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                List<PoleInfo> poleList = poles ? Poles.Collect(tr, modelSpace).Where(p => p.Data != null).ToList() : new List<PoleInfo>();
                List<BoxInfo> boxList = boxes ? Boxes.Collect(tr, modelSpace) : new List<BoxInfo>();

                string what = string.Join(" e ", new[]
                {
                    poleList.Count > 0 ? $"{poleList.Count} poste(s)" : null,
                    boxList.Count > 0 ? $"{boxList.Count} CTO/CEO" : null
                }.Where(t => t != null));

                if (what.Length == 0 || !CadHelpers.AskYes(ed, $"\nAtualizar os nomes de {what} deste desenho para o padrão novo? [Sim/Nao] <Sim>: "))
                {
                    tr.Commit();
                    return;
                }

                int updated = 0, locked = 0;
                foreach (PoleInfo pole in poleList)
                {
                    if (Rename(tr, pole.Id, PoleData.NumberText(pole.Data!.Number),
                            (br, space) => PoleLabels.Place(tr, db, space, br, pole.Data!))) updated++;
                    else locked++;
                }
                foreach (BoxInfo box in boxList)
                {
                    if (Rename(tr, box.Id, box.Data.Id, (br, space) => PoleLabels.PlaceBox(tr, db, space, br, box.Data))) updated++;
                    else locked++;
                }
                tr.Commit();

                ed.WriteMessage($"\n[INFO]: {updated} nome(s) atualizado(s).");
                if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} bloco(s) em layer travada ficaram com o nome antigo.");
            }
            ed.Regen();
        }

        /// <summary>Grava o nome no atributo de número e atualiza o texto ao lado. False se o bloco ou o texto está em layer travada.</summary>
        private static bool Rename(Transaction tr, ObjectId id, string name, Action<BlockReference, BlockTableRecord> placeLabel)
        {
            try
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var space = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite);
                CadHelpers.SetAttributes(tr, br, tag => CadHelpers.IsTag(tag, CadHelpers.NumberTags) ? name : null);
                placeLabel(br, space);
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.OnLockedLayer)
            {
                return false;
            }
        }
    }
}
