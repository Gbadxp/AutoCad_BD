using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace FiberPlugin.Commands
{
    public class InsertPoleCommand
    {
        // Persiste o último número utilizado na sessão do AutoCAD
        private static int _lastPoleCounter = 1;

        // Comando exclusivo para inserir postes com numeração sequencial
        [CommandMethod("FIBRA_INSERIR_POSTE")]
        public void InsertPole()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string blockName = null;

            // 1. Busca automaticamente o bloco que representa o Poste
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (!btr.IsLayout && !btr.IsAnonymous && !btr.Name.StartsWith("*"))
                    {
                        if (btr.Name.ToUpperInvariant().Contains("POSTE"))
                        {
                            blockName = btr.Name;
                            break; // Encontrou o bloco do poste
                        }
                    }
                }
                tr.Commit();
            }

            if (string.IsNullOrEmpty(blockName))
            {
                ed.WriteMessage("\n[ERRO]: Nenhum bloco com o nome 'POSTE' (ou contendo 'POSTE') foi encontrado no desenho atual!");
                return;
            }

            int poleCounter = 1;

            if (_lastPoleCounter > 1)
            {
                PromptKeywordOptions pko = new PromptKeywordOptions($"\nDeseja continuar a contagem do poste N° {_lastPoleCounter} ou Iniciar do começo? [Continuar/Iniciar] ", "Continuar Iniciar");
                pko.Keywords.Default = "Continuar";
                pko.AllowNone = true;
                
                PromptResult pkr = ed.GetKeywords(pko);
                if (pkr.Status == PromptStatus.Cancel) return;

                if (pkr.StringResult == "Continuar" || pkr.Status == PromptStatus.None)
                {
                    poleCounter = _lastPoleCounter;
                }
                else
                {
                    PromptIntegerOptions pio = new PromptIntegerOptions("\nDigite o novo número sequencial inicial (ex: 1): ");
                    pio.DefaultValue = 1;
                    PromptIntegerResult pir = ed.GetInteger(pio);
                    if (pir.Status != PromptStatus.OK) return;

                    poleCounter = pir.Value;
                }
            }
            else
            {
                PromptIntegerOptions pio = new PromptIntegerOptions("\nDigite o número sequencial inicial (ex: 1): ");
                pio.DefaultValue = 1;
                PromptIntegerResult pir = ed.GetInteger(pio);
                if (pir.Status != PromptStatus.OK) return;

                poleCounter = pir.Value;
            }

            while (true)
            {
                PromptPointOptions ppo = new PromptPointOptions($"\nSelecione o ponto de inserção para o Poste #{poleCounter} (ou aperte ENTER/ESC para sair): ");
                ppo.AllowNone = true;
                
                PromptPointResult pPtRes = ed.GetPoint(ppo);
                
                if (pPtRes.Status == PromptStatus.Cancel || pPtRes.Status == PromptStatus.None) 
                {
                    break;
                }
                
                if (pPtRes.Status != PromptStatus.OK) continue;

                Point3d insertionPoint = pPtRes.Value;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                if (!bt.Has(blockName))
                {
                    ed.WriteMessage($"\nErro: O bloco '{blockName}' não foi encontrado no desenho atual.");
                    tr.Abort();
                    return;
                }

                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                using (BlockReference blockRef = new BlockReference(insertionPoint, btr.ObjectId))
                {
                    blockRef.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);

                    currentSpace.AppendEntity(blockRef);
                    tr.AddNewlyCreatedDBObject(blockRef, true);

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
                                    attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                                    
                                    string tagStr = attDef.Tag.ToUpperInvariant().Trim();
                                    
                                    if (tagStr == "NÚMERO" || tagStr == "NUMERO" || tagStr == "ID")
                                    {
                                        attRef.TextString = $"N° {poleCounter}";
                                    }
                                    else if (tagStr == "COORDENADA_Y" || tagStr == "COORDENADA Y")
                                    {
                                        attRef.TextString = $"{insertionPoint.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} m S";
                                    }
                                    else if (tagStr == "COORDENADA_X" || tagStr == "COORDENADA X")
                                    {
                                        attRef.TextString = $"{insertionPoint.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} m E";
                                    }

                                    blockRef.AttributeCollection.AppendAttribute(attRef);
                                    tr.AddNewlyCreatedDBObject(attRef, true);
                                }
                            }
                        }
                    }
                }

                poleCounter++;
                tr.Commit();
                ed.UpdateScreen();
                
                ed.WriteMessage($"\n[AVISO]: Poste inserido com sucesso em X:{insertionPoint.X:F2}, Y:{insertionPoint.Y:F2}.");
            } // Fim da transação
            } // Fim do while(true)
            
            _lastPoleCounter = poleCounter;
            
            ed.WriteMessage($"\n[AVISO]: Inserção de postes finalizada. Próximo poste será o N° {_lastPoleCounter}");
        }
    }
}
