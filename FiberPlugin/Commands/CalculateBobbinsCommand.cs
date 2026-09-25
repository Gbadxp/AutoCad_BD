using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class CalculateBobbinsCommand
    {
        [CommandMethod("FIBRA_CALCULAR_BOBINAS")]
        public void CalculateBobbins()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            Dictionary<string, double> cableLengths;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                cableLengths = CadHelpers.CableLengths(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                tr.Commit();
            }

            if (cableLengths.Count == 0)
            {
                ed.WriteMessage("\n[AVISO]: Nenhum cabo de fibra encontrado no desenho.");
                return;
            }

            // Pede o tamanho da bobina
            PromptDoubleOptions pdoBobbin = new PromptDoubleOptions("\nInforme o tamanho da bobina em metros: ");
            pdoBobbin.DefaultValue = 2000.0;
            pdoBobbin.UseDefaultValue = true;
            pdoBobbin.AllowZero = false;
            pdoBobbin.AllowNegative = false;

            PromptDoubleResult pdrBobbin = ed.GetDouble(pdoBobbin);
            if (pdrBobbin.Status != PromptStatus.OK) return;

            double bobbinSize = pdrBobbin.Value;

            // Pede a margem de segurança
            PromptDoubleOptions pdoMargin = new PromptDoubleOptions("\nInforme a margem de segurança em porcentagem (%): ");
            pdoMargin.DefaultValue = 5.0; // Padrão de 5% de quebra/sobra
            pdoMargin.UseDefaultValue = true;
            pdoMargin.AllowNegative = false;

            PromptDoubleResult pdrMargin = ed.GetDouble(pdoMargin);
            if (pdrMargin.Status != PromptStatus.OK) return;

            double marginPercent = pdrMargin.Value;

            // Exibir resultados
            ed.WriteMessage("\n\n================================================");
            ed.WriteMessage("\n           RESUMO DE CABOS E BOBINAS            ");
            ed.WriteMessage("\n================================================");
            ed.WriteMessage($"\nTamanho da Bobina: {bobbinSize:F2} m");
            ed.WriteMessage($"\nMargem de Segurança: {marginPercent}%\n");

            foreach(var kvp in cableLengths.OrderBy(x => x.Key))
            {
                string cableName = kvp.Key;
                double totalLength = kvp.Value;
                double lengthWithMargin = totalLength * (1 + (marginPercent / 100.0));
                double bobbinsNeeded = lengthWithMargin / bobbinSize;
                int bobbinsRoundedUp = (int)Math.Ceiling(bobbinsNeeded);

                ed.WriteMessage($"\n--- {cableName} ---");
                ed.WriteMessage($"\nMetragem Total: {totalLength:F2} m  |  C/ Margem: {lengthWithMargin:F2} m");
                ed.WriteMessage($"\nBobinas Necessárias: {bobbinsRoundedUp} bobina(s)\n");
            }
            ed.WriteMessage("\n================================================\n");
        }
    }
}
