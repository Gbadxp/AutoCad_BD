using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        // Últimos dados usados, sugeridos na próxima vez (durante a sessão do AutoCAD)
        private static string _lastType = PoleData.DoubleT;
        private static double _lastHeight = 11;
        private static double _lastEffort = 300;

        /// <summary>
        /// Identifica postes: qualquer bloco clicado recebe número, tipo (DT/CC), altura e esforço, e ganha
        /// ao lado o texto com numeração, altura/esforço e coordenada UTM. O tipo não aparece no desenho,
        /// só na listagem de postes (FIBRA_LISTA_POSTES).
        /// </summary>
        [CommandMethod("FIBRA_NOMEAR_POSTE")]
        public void NamePoles()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            if (!AskPoleData(ed)) return;

            // Números já usados no desenho, para nunca repetir
            HashSet<int> used;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                used = new HashSet<int>(Poles.Collect(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead))
                    .Select(p => Poles.ParseNumber(p.Number) ?? 0));
                tr.Commit();
            }

            int? start = AskNumber(ed, NextFree(used, 1));
            if (start == null) return;
            int number = start.Value;

            ed.WriteMessage($"\n[INFO]: Postes {_lastType} {Format(_lastHeight)}/{Format(_lastEffort)}. " +
                            "Clique nos blocos em sequência. N = mudar número, D = mudar dados, Enter = sair.");

            while (true)
            {
                number = NextFree(used, number);

                var peo = new PromptEntityOptions(
                    $"\nPoste {PoleData.NumberText(number)} ({_lastType} {Format(_lastHeight)}/{Format(_lastEffort)}): " +
                    "clique no bloco [Numero/Dados]: ", "Numero Dados")
                {
                    AllowNone = true
                };
                peo.SetRejectMessage("\nSelecione um bloco.");
                peo.AddAllowedClass(typeof(BlockReference), false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.Cancel || per.Status == PromptStatus.None) break;

                if (per.Status == PromptStatus.Keyword)
                {
                    if (per.StringResult == "Numero")
                    {
                        int? n = AskNumber(ed, number);
                        if (n != null) number = n.Value;
                    }
                    else if (per.StringResult == "Dados")
                    {
                        AskPoleData(ed);
                    }
                    continue;
                }
                if (per.Status != PromptStatus.OK) continue;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForWrite);
                    var space = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForWrite);

                    // Poste já identificado mantém o número; só os dados e o texto são atualizados
                    PoleData? existing = XDataTags.ReadPole(br);
                    var data = new PoleData
                    {
                        Number = existing?.Number ?? number,
                        Type = _lastType,
                        HeightM = _lastHeight,
                        EffortDaN = _lastEffort
                    };

                    XDataTags.TagPole(tr, db, br, data);
                    UpdateAttributes(tr, br, data);
                    PoleLabels.Place(tr, db, space, br, data);
                    tr.Commit();

                    if (existing != null)
                    {
                        ed.WriteMessage($"\n[ATUALIZADO]: {PoleData.NumberText(data.Number)} agora é {data.Designation}.");
                    }
                    else
                    {
                        used.Add(number);
                        ed.WriteMessage($"\n[OK]: {PoleData.NumberText(number)} | {data.Designation} | E {br.Position.X:F2} N {br.Position.Y:F2}");
                    }
                }
            }
        }

        /// <summary>Tipo, altura e esforço (sugere os últimos usados). Retorna false se cancelar.</summary>
        private static bool AskPoleData(Editor ed)
        {
            var pko = new PromptKeywordOptions($"\nTipo do poste [DT/CC] (DT = Duplo T, CC = Circular) <{_lastType}>: ", "DT CC")
            {
                AllowNone = true
            };
            PromptResult typeRes = ed.GetKeywords(pko);
            if (typeRes.Status == PromptStatus.Cancel) return false;

            var pdoHeight = new PromptDoubleOptions($"\nAltura do poste em metros (ex.: 9, 10, 11, 12) <{Format(_lastHeight)}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult heightRes = ed.GetDouble(pdoHeight);
            if (heightRes.Status == PromptStatus.Cancel) return false;

            var pdoEffort = new PromptDoubleOptions($"\nEsforço nominal em daN (ex.: 150, 200, 300, 600) <{Format(_lastEffort)}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult effortRes = ed.GetDouble(pdoEffort);
            if (effortRes.Status == PromptStatus.Cancel) return false;

            if (typeRes.Status == PromptStatus.OK) _lastType = typeRes.StringResult;
            if (heightRes.Status == PromptStatus.OK) _lastHeight = heightRes.Value;
            if (effortRes.Status == PromptStatus.OK) _lastEffort = effortRes.Value;
            return true;
        }

        private static int? AskNumber(Editor ed, int suggested)
        {
            var pio = new PromptIntegerOptions($"\nNúmero do próximo poste <{suggested}>: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptIntegerResult res = ed.GetInteger(pio);
            if (res.Status == PromptStatus.Cancel) return null;
            return res.Status == PromptStatus.OK ? res.Value : suggested;
        }

        private static int NextFree(HashSet<int> used, int from)
        {
            int n = Math.Max(1, from);
            while (used.Contains(n)) n++;
            return n;
        }

        /// <summary>Blocos antigos com atributos: mantém NUMERO e NOME preenchidos também.</summary>
        private static void UpdateAttributes(Transaction tr, BlockReference br, PoleData data)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                string? value = CadHelpers.IsTag(att.Tag, CadHelpers.NumberTags) ? PoleData.NumberText(data.Number)
                              : CadHelpers.IsTag(att.Tag, CadHelpers.NameTags) ? data.HeightEffort
                              : CadHelpers.CoordinateAttribute(att.Tag, br.Position);
                if (value == null) continue;

                att.UpgradeOpen();
                att.TextString = value;
            }
        }

        private static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
