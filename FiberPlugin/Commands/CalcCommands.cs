using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    /// <summary>Dados do cálculo de esforço: parâmetros do projeto e esforço que já existe nos postes.</summary>
    public class CalcCommands
    {
        /// <summary>Altura de fixação do cabo e origem da tração (Tabela 08 da NDU 009 ou peso), gravadas no DWG.</summary>
        [CommandMethod("FIBRA_PARAMETROS")]
        public void SetParameters()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            CalcSettings? settings = CalcSettings.Ask(ed, doc.Database);
            if (settings == null) return;

            ed.WriteMessage($"\n[SUCESSO]: Cabo a {settings.AttachHeightM:0.00} m do solo; tração: {settings.MethodText}.");
            ed.WriteMessage("\n[DICA]: Rode o Esforço no Percurso de novo para atualizar as setas já desenhadas.");
        }

        /// <summary>
        /// Esforço que já existe nos postes selecionados (redes de baixa e média tensão da Energisa e cabos de
        /// outras ocupantes), em kgf a 20 cm do topo. Ele é somado ao esforço do projeto na comparação com o nominal.
        /// </summary>
        [CommandMethod("FIBRA_ESFORCO_EXISTENTE", CommandFlags.UsePickSet)]
        public void SetExistingEffort()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelecione o(s) poste(s): " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK) return;

            // Só postes inseridos pelo plugin guardam o esforço existente
            var poles = new List<(ObjectId Id, PoleData Data)>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in psr.Value.GetObjectIds())
                {
                    if (XDataTags.ReadPole((Entity)tr.GetObject(id, OpenMode.ForRead)) is PoleData data) poles.Add((id, data));
                }
            }
            if (poles.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum poste do Inserir Postes na seleção.");
                return;
            }

            double current = poles[0].Data.ExistingKgf;
            var pdo = new PromptDoubleOptions(
                $"\nEsforço existente a 20 cm do topo, em kgf (redes da Energisa e outras ocupantes) <{current:0.##}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = true
            };
            PromptDoubleResult res = ed.GetDouble(pdo);
            if (res.Status == PromptStatus.Cancel) return;
            double value = res.Status == PromptStatus.OK ? res.Value : current;

            int changed = 0, locked = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (id, data) in poles)
                {
                    try
                    {
                        var br = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                        data.ExistingKgf = value;
                        XDataTags.TagPole(tr, db, br, data);
                        changed++;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.OnLockedLayer)
                    {
                        locked++;
                    }
                }
                tr.Commit();
            }

            ed.WriteMessage($"\n[OK]: {changed} poste(s) com esforço existente de {value:0.##} kgf.");
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} poste(s) em layer travada não foram alterados.");
            if (psr.Value.Count > poles.Count) ed.WriteMessage($"\n[INFO]: {psr.Value.Count - poles.Count} bloco(s) que não são postes foram ignorados.");
        }
    }
}
