using System.IO;
using FiberPlugin.Models;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Até a 1.9.35 cabos, postes, tração e empresa eram planilhas da pasta Dados (cabos.csv, postes.csv,
    /// tracao_ndu009.csv, empresa.txt), editadas no Excel. Ao carregar o plugin instalado, as que ainda não foram
    /// convertidas viram os arquivos do plugin em Documentos\Fiber Plugin e as antigas vão para Dados\Antigos.
    /// Planilha que não der para ler fica onde está (continua sendo lida até a próxima vez).
    /// </summary>
    public static class LegacyData
    {
        /// <returns>Nomes das planilhas convertidas agora (vazia se não havia nada a converter).</returns>
        public static List<string> Migrate()
        {
            var converted = new List<string>();
            if (!PluginPaths.IsInstalled) return converted;

            var warnings = new List<string>();
            Convert(converted, CableProvider.FileName, CableProvider.LegacyFileName, () =>
            {
                List<CableModel> cables = CableProvider.Read(warnings, out string? error);
                return error == null && cables.Count > 0 ? CableProvider.Save(cables) ?? "" : null;
            });
            Convert(converted, PoleModels.FileName, PoleModels.LegacyFileName, () =>
            {
                List<PoleData> poles = PoleModels.Read(warnings, out string? error);
                return error == null && poles.Count > 0 ? PoleModels.Save(poles) ?? "" : null;
            });
            Convert(converted, TractionTable.FileName, TractionTable.LegacyFileName, () =>
            {
                TractionTable table = TractionTable.Read(warnings, out string? error);
                return error == null ? table.Save() ?? "" : null;
            });
            Convert(converted, CompanyInfo.FileName, CompanyInfo.FileName, () =>
            {
                CompanyInfo company = CompanyInfo.Read();
                return company.IsEmpty ? null : company.Save() ?? "";
            });
            return converted;
        }

        /// <param name="readAndSave">"" = convertida; null = não deu para ler; outro texto = erro ao gravar.</param>
        private static void Convert(List<string> converted, string fileName, string legacyName, Func<string?> readAndSave)
        {
            if (File.Exists(DataFiles.UserFile(fileName))) return;
            string? legacy = PluginPaths.DataFile(legacyName);
            if (legacy == null || !File.Exists(legacy)) return;
            try
            {
                if (readAndSave() == "") converted.Add(legacyName);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
