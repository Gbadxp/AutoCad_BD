using System.IO;
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
        /// Janela Configurações: dados do projeto deste desenho (percurso, contrato, ART, zona UTM, escala, pasta de dados e
        /// Atualizar Blocos), cadastros do plugin (cabos com cor, tipo de linha e espessura, modelos de poste, tabela de
        /// tração e dados da empresa) e preferências (nomes, tamanhos, cores e a layer das ruas). Depois de salvar, leva as
        /// mudanças para o desenho aberto: dados do projeto, zona e escala (com a atualização dos postes e anotações),
        /// aparência das layers que já existem (e o nome da layer das ruas) e, se o usuário confirmar, os nomes e a altura
        /// dos textos já desenhados.
        /// </summary>
        [CommandMethod("FIBRA_CONFIGURACOES")]
        public void EditSettings() => Open(UI.SettingsForm.TabProject);

        /// <summary>O antigo botão Dados do Projeto: a mesma janela, na aba Projeto (continua valendo na linha de comando).</summary>
        [CommandMethod("FIBRA_DADOS_PROJETO")]
        public void EditProjectData() => Open(UI.SettingsForm.TabProject);

        private static void Open(int initialTab)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            ProjectInfo projectBefore = ProjectInfo.Load(db);
            UtmSettings? zoneBefore = UtmZone.Get(db);
            int scaleBefore = DrawingScale.Get(db);
            ElementScales elementsBefore = DrawingScale.GetElements(db);

            UserSettings.Reload();
            UserSettings before = UserSettings.Current.Clone();

            var cableWarnings = new List<string>();
            List<CableModel> cablesBefore = CableProvider.Read(cableWarnings, out string? cableError);
            var poleWarnings = new List<string>();
            List<PoleData> polesBefore = PoleModels.Read(poleWarnings, out string? poleError);
            var tractionWarnings = new List<string>();
            TractionTable tractionBefore = TractionTable.Read(tractionWarnings, out string? tractionError);
            CompanyInfo companyBefore = CompanyInfo.Read();
            Dictionary<string, string> shortcutsBefore = ShortcutSettings.Load();

            // O que já foi gravado (a gravação pode parar no meio, ex.: arquivo bloqueado, e o usuário cancelar depois):
            // o desenho recebe o que foi gravado mesmo assim, para cadastro e desenho não ficarem diferentes
            List<CableModel>? savedCables = null;
            Dictionary<string, string>? savedShortcuts = null;
            UserSettings? savedSettings = null;

            bool confirmed;
            ProjectInfo project;
            int scale;
            ElementScales elements;
            bool updateBlocks, updateCoordinates;
            using (var form = new UI.SettingsForm(cablesBefore, polesBefore, tractionBefore, companyBefore, before,
                       projectBefore, scaleBefore, zoneBefore != null,
                       Notes(cableError, cableWarnings), Notes(poleError, poleWarnings), Notes(tractionError, tractionWarnings), initialTab,
                       shortcutsBefore, ShortcutRegistry.Conflict, elementsBefore)
                   { OpenDataFolder = PluginCommands.ShowDataFolder })
            {
                // Grava o cadastro que mudou ou que ainda não foi convertido da planilha antiga
                form.SaveChanges = f =>
                {
                    if (Pending(CableProvider.FileName, CableProvider.ToText(f.Cables) != CableProvider.ToText(cablesBefore)))
                    {
                        if (CableProvider.Save(f.Cables) is string e1) return e1;
                        savedCables = f.Cables;
                    }
                    if (Pending(PoleModels.FileName, PoleModels.ToText(f.PoleTypes) != PoleModels.ToText(polesBefore)) &&
                        PoleModels.Save(f.PoleTypes) is string e2) return e2;
                    if (Pending(TractionTable.FileName, !f.Traction.SameAs(tractionBefore)) && f.Traction.Save() is string e3) return e3;
                    if (Pending(CompanyInfo.FileName, !f.Company.SameAs(companyBefore)) && !(f.Company.IsEmpty && companyBefore.IsEmpty) &&
                        f.Company.Save() is string e4) return e4;
                    if (!ShortcutSettings.Same(f.Shortcuts, shortcutsBefore))
                    {
                        if (ShortcutSettings.Save(f.Shortcuts) is string e6) return e6;
                        savedShortcuts = f.Shortcuts;
                    }
                    if (f.Settings.Save() is string e5) return $"{UserSettings.FilePath}: {e5}";
                    savedSettings = f.Settings;
                    return null;
                };
                confirmed = AcApp.ShowModalDialog(form) == System.Windows.Forms.DialogResult.OK;
                project = form.Project;
                scale = form.ScaleDenominator;
                elements = form.ElementScales;
                updateBlocks = confirmed && form.UpdateBlocksAfter;
                updateCoordinates = confirmed && form.UpdateCoordinatesAfter;
            }

            if (!confirmed)
            {
                if (savedCables == null && savedShortcuts == null && savedSettings == null) return;
                ed.WriteMessage("\n[AVISO]: A janela foi fechada sem salvar tudo; o que já tinha sido gravado vale e foi levado para o desenho.");
            }
            UserSettings after = savedSettings ?? before;
            List<CableModel> cablesAfter = savedCables ?? cablesBefore;

            // Atalhos novos valem na hora, e as dicas dos botões da aba Fibra mostram o atalho
            if (savedShortcuts != null)
            {
                List<string> skipped = ShortcutRegistry.Apply(savedShortcuts);
                ed.WriteMessage($"\n[SUCESSO]: Atalhos atualizados ({savedShortcuts.Count(s => s.Value.Length > 0)} comandos com atalho).");
                foreach (string warning in skipped) ed.WriteMessage($"\n[AVISO]: Atalho não registrado: {warning}");
                UI.FiberRibbon.RefreshShortcuts();
            }

            // Aba Projeto: grava só se algo mudou (ou para o Atualizar Blocos/Coordenadas), como fazia a janela Dados do Projeto
            if (confirmed && (updateBlocks || updateCoordinates || !project.SameAs(projectBefore) || scale != scaleBefore ||
                              !elements.SameAs(elementsBefore)))
                ApplyProject(doc, project, zoneBefore, scaleBefore, elementsBefore, scale, elements, updateCoordinates);

            if (confirmed)
            {
                ed.WriteMessage($"\n[SUCESSO]: Configurações salvas ({cablesAfter.Count} cabo(s), nomes {after.Name(after.PolePrefix, 1)}, " +
                                $"{after.Name(after.CtoPrefix, 1)}, {after.Name(after.CeoPrefix, 1)}, ruas na layer {after.RoadLayer}). " +
                                "Valem para todos os desenhos.");
            }

            UpdateLayers(ed, db, cablesBefore, cablesAfter, before, after);

            bool poleNames = before.PolePrefix != after.PolePrefix || before.NumberDigits != after.NumberDigits;
            bool boxNames = before.CtoPrefix != after.CtoPrefix || before.CeoPrefix != after.CeoPrefix || before.NumberDigits != after.NumberDigits;
            if (poleNames || boxNames) UpdateNames(ed, db, poleNames, boxNames);

            if (Math.Abs(after.TextHeight - before.TextHeight) > 1e-9) ScaleCommand.ResizeTexts(ed, db, after.TextHeight / before.TextHeight);

            if (updateBlocks) doc.SendStringToExecute("FIBRA_ATUALIZAR_BLOCOS ", true, false, false);
        }

        /// <summary>
        /// Grava os dados do projeto (no desenho e no projeto.txt). Com zona nova (ou o botão Atualizar Coordenadas),
        /// grava a zona e reescreve as coordenadas dos postes e blocos sem perguntar; com escala nova (do desenho ou de
        /// algum elemento), grava e oferece ajustar o que já está desenhado (mesmo caminho dos comandos FIBRA_ZONA_UTM,
        /// FIBRA_ATUALIZAR_COORDENADAS e FIBRA_ESCALA).
        /// </summary>
        private static void ApplyProject(Document doc, ProjectInfo info, UtmSettings? zoneBefore, int scaleBefore, ElementScales elementsBefore,
            int scale, ElementScales elements, bool updateCoordinates)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string? error = info.Save(db);
            ed.WriteMessage("\n[SUCESSO]: Dados do projeto salvos. O Memorial Descritivo, o de Esforço e as Coordenadas já abrem com eles preenchidos.");
            if (error != null) ed.WriteMessage($"\n[AVISO]: Salvos só neste desenho; não foi possível gravar {ProjectInfo.FilePath} ({error}).");

            // Zona nova (ou definida agora): grava no desenho e atualiza as coordenadas
            UtmSettings? zone = info.Zone;
            bool zoneChanged = zone != null && (zoneBefore == null || zoneBefore.Zone != zone.Zone || zoneBefore.South != zone.South);
            if (zoneChanged)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    UtmZone.Set(tr, db, zone!);
                    tr.Commit();
                }
                ed.WriteMessage($"\n[SUCESSO]: Zona UTM do projeto: {zone!.Zone}, hemisfério {(zone.South ? "Sul" : "Norte")}.");
            }
            if (zone != null && (zoneChanged || updateCoordinates)) UtmZoneCommand.UpdateCoordinates(ed, db, zone);
            else if (updateCoordinates) ed.WriteMessage("\n[AVISO]: Defina a zona UTM do projeto (ou cole a coordenada do local) para atualizar as coordenadas.");

            // Escala nova, do desenho ou de algum elemento: grava e oferece ajustar o que já está desenhado
            if (scale != scaleBefore || !elements.SameAs(elementsBefore)) ScaleCommand.Apply(ed, db, scaleBefore, elementsBefore, scale, elements);
        }

        /// <summary>Precisa gravar: mudou na janela ou ainda está na planilha antiga (sem o arquivo do plugin).</summary>
        private static bool Pending(string fileName, bool changed) => changed || !File.Exists(DataFiles.UserFile(fileName));

        /// <summary>Aviso mostrado na aba do cadastro (null se ele foi lido sem problemas).</summary>
        private static string? Notes(string? error, List<string> warnings)
        {
            if (error != null) return error;
            if (warnings.Count == 0) return null;
            return $"{warnings.Count} linha(s) do arquivo antigo foram ignoradas na leitura ({warnings[0]}) e não estão na tabela.";
        }

        /// <summary>
        /// Leva para o desenho aberto a aparência nova das layers do plugin que já existem: a de cada cabo (cor, tipo de
        /// linha e espessura), a das ruas (renomeada, se o nome mudou) e a cor das layers de textos e setas.
        /// </summary>
        private static void UpdateLayers(Editor ed, Database db, List<CableModel> cablesBefore, List<CableModel> cablesAfter,
            UserSettings before, UserSettings after)
        {
            var styles = new Dictionary<string, LayerStyle>(StringComparer.OrdinalIgnoreCase);
            foreach (CableModel cable in cablesAfter)
            {
                CableModel? old = CableProvider.Find(cablesBefore, cable.ShortName);
                LayerStyle style = CableDrawing.StyleFor(cable, after);
                if (old == null || !CableDrawing.StyleFor(old, before).SameAs(style)) styles[CableDrawing.LayerFor(cable)] = style;
            }
            bool roadRenamed = !before.RoadLayer.Equals(after.RoadLayer, StringComparison.OrdinalIgnoreCase);
            if (roadRenamed || !before.RoadStyle.SameAs(after.RoadStyle)) styles[after.RoadLayer] = after.RoadStyle;

            var colors = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            if (before.PoleLabelColor != after.PoleLabelColor) colors[PoleLabels.Layer] = after.PoleLabelColor;
            if (before.BoxLabelColor != after.BoxLabelColor) colors[PoleLabels.BoxLayer] = after.BoxLabelColor;
            if (before.EffortColor != after.EffortColor) colors[FiberSettings.EffortLayer] = after.EffortColor;
            if (styles.Count == 0 && colors.Count == 0) return;

            int changed = 0;
            var warnings = new List<string>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                // Nome novo da layer das ruas: a layer antiga deste desenho passa a ter o nome novo. Uma recusa do AutoCAD
                // (ex.: configuracoes.txt antigo com a layer 0) não impede o resto
                if (roadRenamed && layers.Has(before.RoadLayer) && !layers.Has(after.RoadLayer) &&
                    LayerStyle.LayerNameError(before.RoadLayer) == null)
                {
                    try
                    {
                        var road = (LayerTableRecord)tr.GetObject(layers[before.RoadLayer], OpenMode.ForWrite);
                        road.Name = after.RoadLayer;
                        ed.WriteMessage($"\n[INFO]: Layer {before.RoadLayer} renomeada para {after.RoadLayer}.");
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        warnings.Add($"Não foi possível renomear a layer {before.RoadLayer} para {after.RoadLayer} ({ex.Message}).");
                    }
                }

                foreach (var (name, style) in styles.Select(s => (s.Key, s.Value)))
                {
                    if (!layers.Has(name)) continue;
                    try
                    {
                        if (CadHelpers.ApplyLayerStyle(tr, db, name, style) is string warning) warnings.Add(warning);
                        changed++;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        warnings.Add($"Não foi possível mudar a layer {name} ({ex.Message}).");
                    }
                }
                foreach (var (name, color) in colors.Select(c => (c.Key, c.Value)))
                {
                    if (!layers.Has(name)) continue;
                    var layer = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForWrite);
                    layer.Color = AcColor.FromColorIndex(AcColorMethod.ByAci, color);
                    changed++;
                }
                tr.Commit();
            }
            foreach (string warning in warnings) ed.WriteMessage($"\n[AVISO]: {warning}");
            if (changed > 0)
            {
                ed.WriteMessage($"\n[INFO]: Aparência nova em {changed} layer(s) deste desenho.");
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
                Dictionary<string, List<ObjectId>> labels = PoleLabels.IndexLabels(tr, modelSpace);
                foreach (PoleInfo pole in poleList)
                {
                    if (Rename(tr, pole.Id, PoleData.NumberText(pole.Data!.Number),
                            (br, space) => PoleLabels.Place(tr, db, space, br, pole.Data!, labels))) updated++;
                    else locked++;
                }
                foreach (BoxInfo box in boxList)
                {
                    if (Rename(tr, box.Id, box.Data.Id, (br, space) => PoleLabels.PlaceBox(tr, db, space, br, box.Data, labels))) updated++;
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
