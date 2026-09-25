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
    public class PoleListCommand
    {
        /// <summary>
        /// Listagem de postes (CSV): número, tipo (DT/CC), altura, esforço e coordenadas UTM de cada poste,
        /// mais o resumo com a quantidade por tipo. É aqui que o tipo DT/CC aparece.
        /// </summary>
        [CommandMethod("FIBRA_LISTA_POSTES")]
        public void ListPoles()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<PoleInfo> poles;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                poles = Poles.Collect(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead))
                    .OrderBy(p => Poles.ParseNumber(p.Number) ?? int.MaxValue)
                    .ThenBy(p => p.Number)
                    .ToList();
                tr.Commit();
            }

            if (poles.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum poste identificado. Use FIBRA_NOMEAR_POSTE nos blocos dos postes.");
                return;
            }

            int withoutData = poles.Count(p => p.Data == null);

            bool saved = CadHelpers.SaveCsv(ed, "Salvar Listagem de Postes", "Listagem_Postes.csv", sw =>
            {
                sw.WriteLine("--- LISTAGEM DE POSTES ---");
                sw.WriteLine("Número;Tipo;Descrição do Tipo;Altura (m);Esforço Nominal (daN);Poste;Coordenada E;Coordenada N");

                foreach (PoleInfo pole in poles)
                {
                    PoleData? d = pole.Data;
                    sw.WriteLine(string.Join(";",
                        pole.Number,
                        d?.Type ?? "",
                        d?.TypeName ?? "",
                        d != null ? d.HeightM.ToString("0.#", CultureInfo.CurrentCulture) : "",
                        d != null ? d.EffortDaN.ToString("0", CultureInfo.CurrentCulture) : "",
                        d?.Designation ?? pole.Name.Replace(";", ","),
                        pole.Position.X.ToString("F2", CultureInfo.CurrentCulture),
                        pole.Position.Y.ToString("F2", CultureInfo.CurrentCulture)));
                }

                // Resumo para orçamento: quantidade de cada poste (tipo + altura/esforço)
                sw.WriteLine();
                sw.WriteLine("--- RESUMO ---");
                sw.WriteLine("Poste;Tipo;Quantidade");
                foreach (var group in poles.Where(p => p.Data != null)
                                           .GroupBy(p => p.Data!.Designation)
                                           .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sw.WriteLine($"{group.Key};{group.First().Data!.TypeName};{group.Count()}");
                }
                if (withoutData > 0) sw.WriteLine($"Sem tipo definido;;{withoutData}");
                sw.WriteLine($"TOTAL;;{poles.Count}");
            });

            if (!saved) return;

            ed.WriteMessage($"\n[INFO]: {poles.Count} poste(s) na listagem.");
            if (withoutData > 0)
            {
                ed.WriteMessage($"\n[AVISO]: {withoutData} poste(s) sem tipo/altura/esforço. Use FIBRA_NOMEAR_POSTE neles.");
            }
        }
    }
}
