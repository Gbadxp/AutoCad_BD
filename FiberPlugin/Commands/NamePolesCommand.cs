using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class NamePolesCommand
    {
        [CommandMethod("FIBRA_NOMEAR_POSTE")]
        public void NamePoles()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Pergunta o Tipo (CC ou DT)
            PromptKeywordOptions pkoType = new PromptKeywordOptions("\nTipo do Poste [CC/DT] <DT>: ", "CC DT");
            pkoType.AllowNone = true;
            PromptResult prType = ed.GetKeywords(pkoType);
            
            if (prType.Status == PromptStatus.Cancel) return;
            string type = (prType.Status == PromptStatus.None) ? "DT" : prType.StringResult;

            // 2. Pergunta a Altura
            PromptIntegerOptions pioHeight = new PromptIntegerOptions("\nAltura do Poste (Ex: 10, 11, 12) <11>: ");
            pioHeight.AllowNone = true;
            PromptIntegerResult pirHeight = ed.GetInteger(pioHeight);
            
            if (pirHeight.Status == PromptStatus.Cancel) return;
            int height = (pirHeight.Status == PromptStatus.None) ? 11 : pirHeight.Value;

            // 3. Pergunta o Esforço
            PromptIntegerOptions pioEffort = new PromptIntegerOptions("\nEsforço do Poste (Ex: 150, 200, 300, 600) <200>: ");
            pioEffort.AllowNone = true;
            PromptIntegerResult pirEffort = ed.GetInteger(pioEffort);
            
            if (pirEffort.Status == PromptStatus.Cancel) return;
            int effort = (pirEffort.Status == PromptStatus.None) ? 200 : pirEffort.Value;

            // Monta o nome. Ex: DT 11/200
            string fullName = $"{type} {height}/{effort}";

            ed.WriteMessage($"\n[INICIANDO] Configurado para marcar postes como: {fullName}");
            ed.WriteMessage("\n[DICA] Pressione ESC a qualquer momento para sair.");

            // 4. Loop infinito para sair clicando nos postes
            while (true)
            {
                PromptEntityOptions peo = new PromptEntityOptions($"\nSelecione um bloco de poste para nomear como '{fullName}': ");
                peo.SetRejectMessage("\nPor favor, selecione apenas blocos!");
                peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);
                peo.AllowNone = true;

                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status == PromptStatus.Cancel || per.Status == PromptStatus.None)
                {
                    break;
                }

                if (per.Status != PromptStatus.OK) continue;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockReference br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    
                    if (br.AttributeCollection.Count == 0)
                    {
                        ed.WriteMessage("\n[ERRO] O bloco selecionado NÃO possui Atributos! Edite o bloco com o comando ATTDEF.");
                        tr.Abort();
                        continue;
                    }

                    bool foundAttribute = false;

                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                        
                        // Procura por Tags comuns usadas para nomear postes
                        if (CadHelpers.IsTag(attRef.Tag, CadHelpers.NameTags))
                        {
                            attRef.UpgradeOpen();
                            attRef.TextString = fullName;
                            foundAttribute = true;
                        }
                    }

                    if (foundAttribute)
                    {
                        ed.WriteMessage($"\n[SUCESSO] Poste atualizado para: {fullName}");
                    }
                    else
                    {
                        ed.WriteMessage("\n[AVISO] Nenhum atributo com a Tag 'NOME', 'INFO', 'TIPO' ou 'DESCRICAO' foi encontrado neste bloco.");
                    }

                    tr.Commit();
                }
            }
        }
    }
}
