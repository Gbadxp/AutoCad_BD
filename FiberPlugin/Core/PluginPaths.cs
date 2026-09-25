using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Localiza as pastas "Dados" (planilhas) e "Blocos" (biblioteca BLOCOS.dwg).
    ///
    /// Desenvolvimento (NETLOAD): procura ao lado da DLL e nas pastas acima dela, então rodando a partir
    /// de bin\Debug\... as pastas do código-fonte (FiberPlugin\Dados e FiberPlugin\Blocos) são usadas direto.
    ///
    /// Instalado (pacote .bundle em ApplicationPlugins):
    ///  - Dados: Documentos\Fiber Plugin\Dados, editável pelo usuário. Arquivos que faltarem lá são
    ///    copiados do pacote (nunca sobrescreve o que o usuário editou).
    ///  - Blocos: a biblioteca BLOCOS.dwg que vem DENTRO do pacote, atualizada a cada instalação.
    ///    Documentos\Fiber Plugin\Blocos é opcional, para blocos pessoais; em nomes repetidos, o bloco
    ///    pessoal tem prioridade.
    /// </summary>
    public static class PluginPaths
    {
        public const string DataFolderName = "Dados";
        public const string BlocksFolderName = "Blocos";
        public const string UserFolderName = "Fiber Plugin";
        private const int MaxLevelsUp = 4;

        private static bool _userFoldersChecked;

        public static string AssemblyDir =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();

        /// <summary>True quando o plugin foi carregado de um pacote .bundle (instalação normal).</summary>
        public static bool IsInstalled => AssemblyDir.IndexOf(".bundle", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Documentos\Fiber Plugin: pasta editável pelo usuário na instalação normal.</summary>
        public static string UserRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), UserFolderName);

        public static string? DataDir
        {
            get
            {
                if (!IsInstalled) return FindUpwards(DataFolderName);
                EnsureUserFolders();
                string dir = Path.Combine(UserRoot, DataFolderName);
                return Directory.Exists(dir) ? dir : FindUpwards(DataFolderName);
            }
        }

        /// <summary>
        /// Pasta onde o usuário grava blocos (e onde o FIBRA_EXPORTAR_BLOCOS salva o BLOCOS.dwg).
        /// Instalado: Documentos\Fiber Plugin\Blocos. Desenvolvimento: a pasta Blocos do código-fonte.
        /// </summary>
        public static string? BlocksDir
        {
            get
            {
                if (!IsInstalled) return FindUpwards(BlocksFolderName);
                EnsureUserFolders();
                return Path.Combine(UserRoot, BlocksFolderName);
            }
        }

        /// <summary>Pastas com bibliotecas de blocos, da maior para a menor prioridade.</summary>
        public static IEnumerable<string> BlockLibraryDirs
        {
            get
            {
                var dirs = new List<string>();
                if (BlocksDir is string user && Directory.Exists(user)) dirs.Add(user);
                if (IsInstalled && FindUpwards(BlocksFolderName) is string package) dirs.Add(package); // Contents\Blocos do pacote
                return dirs.Distinct(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Caminho de um arquivo dentro da pasta Dados (null se a pasta não existir).</summary>
        public static string? DataFile(string fileName)
        {
            string? dir = DataDir;
            return dir == null ? null : Path.Combine(dir, fileName);
        }

        /// <summary>
        /// Prepara Documentos\Fiber Plugin: copia do pacote os arquivos de Dados que ainda não existem lá
        /// (nunca sobrescreve) e cria a pasta Blocos para blocos pessoais.
        /// </summary>
        public static void EnsureUserFolders()
        {
            if (_userFoldersChecked || !IsInstalled) return;
            _userFoldersChecked = true;

            try
            {
                string data = Path.Combine(UserRoot, DataFolderName);
                Directory.CreateDirectory(data);
                string? packageData = FindUpwards(DataFolderName);
                if (packageData != null) CopyMissingFiles(packageData, data);

                string blocks = Path.Combine(UserRoot, BlocksFolderName);
                Directory.CreateDirectory(blocks);
                RemoveLegacyCategoryFolders(blocks);
                WriteUserBlocksReadme(blocks);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string? FindUpwards(string folderName)
        {
            string? dir = AssemblyDir;
            for (int i = 0; i <= MaxLevelsUp && dir != null; i++)
            {
                string candidate = Path.Combine(dir, folderName);
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static void CopyMissingFiles(string source, string target)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target);
                if (!File.Exists(destination)) File.Copy(file, destination);
            }
        }

        /// <summary>
        /// Versões até a 1.1 criavam Blocos\Fibra, \Eletrica, \Postes e \Outros. Remove essas subpastas
        /// quando estão vazias (nunca apaga pasta com arquivos do usuário).
        /// </summary>
        /// <summary>Explica para que serve a pasta de blocos pessoais (substitui o LEIA-ME das versões antigas).</summary>
        private static void WriteUserBlocksReadme(string blocksDir)
        {
            string path = Path.Combine(blocksDir, "LEIA-ME.txt");
            if (File.Exists(path) && File.ReadAllText(path).IndexOf("subpasta", StringComparison.OrdinalIgnoreCase) < 0) return;

            File.WriteAllText(path,
                "BLOCOS PESSOAIS DO FIBER PLUGIN" + Environment.NewLine + Environment.NewLine +
                "Os blocos padrão vêm no BLOCOS.dwg instalado junto com o plugin e são atualizados a cada versão." + Environment.NewLine +
                "Esta pasta é opcional: um BLOCOS.dwg (ou outros .dwg) colocado aqui acrescenta blocos seus." + Environment.NewLine +
                "Se um bloco daqui tiver o mesmo nome de um bloco padrão, o daqui é usado." + Environment.NewLine +
                "O comando FIBRA_EXPORTAR_BLOCOS grava nesta pasta os blocos do desenho aberto." + Environment.NewLine);
        }

        private static void RemoveLegacyCategoryFolders(string blocksDir)
        {
            foreach (string name in new[] { "Fibra", "Eletrica", "Postes", "Outros" })
            {
                string dir = Path.Combine(blocksDir, name);
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
        }
    }
}
