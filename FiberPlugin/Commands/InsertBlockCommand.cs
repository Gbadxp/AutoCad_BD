using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class InsertBlockCommand
    {
        // Este é o nome do comando que você vai digitar no AutoCAD
        [CommandMethod("FIBRA_INSERIR_BLOCO")]
        public void InsertBlock()
        {
            // Obtém os dados do documento ativo no AutoCAD
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<string> blockNames = new List<string>();

            // 1. Busca todos os blocos disponíveis no desenho
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    // Ignora blocos de layout e blocos anônimos gerados dinamicamente (*D, *U, etc)
                    if (!btr.IsLayout && !btr.IsAnonymous && !btr.Name.StartsWith("*"))
                    {
                        // Remove blocos de postes do menu genérico
                        if (!btr.Name.ToUpperInvariant().Contains("POSTE"))
                        {
                            blockNames.Add(btr.Name);
                        }
                    }
                }
                tr.Commit();
            }

            if (blockNames.Count == 0)
            {
                ed.WriteMessage("\n[ERRO]: Nenhum bloco encontrado no desenho atual!");
                return;
            }

            // Ordena os blocos em ordem alfabética
            blockNames.Sort();

            string blockName = null;

            // 2. Mostra a nova janela (UI) com a lista de blocos
            using (var form = new FiberPlugin.UI.BlockSelectionForm(blockNames))
            {
                var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                if (result != System.Windows.Forms.DialogResult.OK) return; // Cancela se o usuário fechar a janela
                
                blockName = form.SelectedBlock;
            }

            if (string.IsNullOrEmpty(blockName)) return;

            while (true)
            {
                // 2. Pede o ponto onde o bloco será inserido
                PromptPointOptions ppo = new PromptPointOptions($"\nSelecione o ponto de inserção para '{blockName}' (ou aperte ENTER/ESC para sair): ");
                ppo.AllowNone = true;
                
                PromptPointResult pPtRes = ed.GetPoint(ppo);
                
                if (pPtRes.Status == PromptStatus.Cancel || pPtRes.Status == PromptStatus.None) 
                {
                    break;
                }
                
                if (pPtRes.Status != PromptStatus.OK) continue;

                Point3d insertionPoint = pPtRes.Value;

                // Inicia uma transação no banco de dados do desenho
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                // 3. Verifica se o bloco realmente existe no desenho atual
                if (!bt.Has(blockName))
                {
                    ed.WriteMessage($"\nErro: O bloco '{blockName}' não foi encontrado no desenho atual.");
                    ed.WriteMessage("\nCertifique-se de que o bloco já existe ou foi importado.");
                    tr.Abort();
                    return;
                }

                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // 4. Cria a referência do bloco para inseri-lo
                using (BlockReference blockRef = new BlockReference(insertionPoint, btr.ObjectId))
                {
                    // Garantimos que o bloco não virá em tamanho zero
                    blockRef.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);

                    currentSpace.AppendEntity(blockRef);
                    tr.AddNewlyCreatedDBObject(blockRef, true);

                    // 5. Verifica se o bloco tem atributos (ex: texto dentro do bloco, como "Número da CTO")
                    if (btr.HasAttributeDefinitions)
                    {
                        foreach (ObjectId id in btr)
                        {
                            DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                            AttributeDefinition attDef = obj as AttributeDefinition;
                            
                            if (attDef != null && !attDef.Constant)
                            {
                                using (AttributeReference attRef = new AttributeReference())
                                {
                                    // Posiciona o atributo junto ao bloco
                                    attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                                    
                                    // AQUI ENTRA A MÁGICA: Podemos automatizar os textos dos atributos
                                    string tagStr = attDef.Tag.ToUpperInvariant().Trim();
                                    
                                    if (tagStr == "COORDENADA_Y" || tagStr == "COORDENADA Y")
                                    {
                                        attRef.TextString = $"{insertionPoint.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} m S";
                                    }
                                    else if (tagStr == "COORDENADA_X" || tagStr == "COORDENADA X")
                                    {
                                        attRef.TextString = $"{insertionPoint.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} m E";
                                    }
                                    else if (tagStr == "TIPO")
                                    {
                                        attRef.TextString = "1x16"; // Capacidade default genérica
                                    }

                                    blockRef.AttributeCollection.AppendAttribute(attRef);
                                    tr.AddNewlyCreatedDBObject(attRef, true);
                                }
                            }
                        }
                    }
                }

                // Finaliza e salva as alterações
                tr.Commit();
                
                // Força o AutoCAD a redesenhar a tela
                ed.UpdateScreen();
                
                ed.WriteMessage($"\n[AVISO]: Bloco '{blockName}' inserido com sucesso em X:{insertionPoint.X:F2}, Y:{insertionPoint.Y:F2}.");
            } // Fim da transação
            } // Fim do while(true)
            
            ed.WriteMessage($"\n[AVISO]: Inserção contínua finalizada. Se os blocos estiverem fora da vista, digite 'Z' e 'E' (Zoom Extents).");
        }
    }
}
