using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class PoleIdCommand
    {
        /// <summary>
        /// ID do poste fornecido pela Energisa (ID_Poste da Tabela A, NDU 009). Clique em cada poste e digite o ID:
        /// ele passa a aparecer no texto do poste e na Tabela A do relatório. Sem botão por enquanto: digite o comando.
        /// </summary>
        [CommandMethod("FIBRA_ID_ENERGISA")]
        public void SetEnergisaId()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            int changed = 0;

            while (true)
            {
                var peo = new PromptEntityOptions("\nSelecione o poste (Enter para terminar): ") { AllowNone = true };
                peo.SetRejectMessage("\nSelecione um poste.");
                peo.AddAllowedClass(typeof(BlockReference), false);
                PromptEntityResult res = ed.GetEntity(peo);
                if (res.Status != PromptStatus.OK) break;

                PoleData? data;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    data = XDataTags.ReadPole((Entity)tr.GetObject(res.ObjectId, OpenMode.ForRead));
                }
                if (data == null)
                {
                    ed.WriteMessage("\n[AVISO]: Este bloco não é um poste do Inserir Postes.");
                    continue;
                }

                string hint = data.EnergisaId.Length > 0 ? $" <{data.EnergisaId}>" : "";
                var pso = new PromptStringOptions($"\nID Energisa de {PoleData.NumberText(data.Number)}{hint}: ") { AllowSpaces = false };
                PromptResult idRes = ed.GetString(pso);
                if (idRes.Status != PromptStatus.OK) break;
                string id = idRes.StringResult.Trim();
                if (id.Length == 0) continue; // Enter mantém o ID atual

                try
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(res.ObjectId, OpenMode.ForWrite);
                        data.EnergisaId = id;
                        XDataTags.TagPole(tr, db, br, data);
                        PoleLabels.Place(tr, db, (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite), br, data);
                        tr.Commit();
                    }
                    changed++;
                    ed.WriteMessage($"\n[OK]: {PoleData.NumberText(data.Number)} → ID {id}");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.OnLockedLayer)
                {
                    ed.WriteMessage("\n[AVISO]: Poste em layer travada.");
                }
            }

            if (changed > 0) ed.WriteMessage($"\n[INFO]: {changed} poste(s) com ID Energisa.");
        }
    }
}
