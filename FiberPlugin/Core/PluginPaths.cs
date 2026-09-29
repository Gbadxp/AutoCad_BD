using System.IO;
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
    ///  - Blocos: o BLOCOS.dwg escolhido no botão Atualizar Blocos (ex.: o da pasta do projeto) ou,
    ///    sem ele, a cópia que vem dentro do pacote.
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

        /// <summary>Pasta Blocos: a do código-fonte (desenvolvimento) ou a do pacote instalado.</summary>
        public static string? BlocksDir => FindUpwards(BlocksFolderName);

        // Arquivo que guarda o BLOCOS.dwg escolhido no botão Atualizar Blocos
        private static string SettingsFile => Path.Combine(UserRoot, "biblioteca.txt");
        private static string? _customLibrary;
        private static bool _customLibraryLoaded;

        /// <summary>
        /// BLOCOS.dwg escolhido pelo usuário (ex.: o da pasta do projeto), lido direto, com prioridade sobre
        /// o do pacote. Assim um bloco novo aparece só de salvar esse arquivo, sem reinstalar o plugin.
        /// Null = usar a biblioteca que vem com o plugin.
        /// </summary>
        public static string? CustomLibrary
        {
            get
            {
                if (!_customLibraryLoaded)
                {
                    _customLibraryLoaded = true;
                    try { _customLibrary = File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null; }
                    catch (IOException) { _customLibrary = null; }
                    if (_customLibrary?.Length == 0) _customLibrary = null;
                }
                return _customLibrary;
            }
            set
            {
                Directory.CreateDirectory(UserRoot);
                if (value == null) File.Delete(SettingsFile);
                else File.WriteAllText(SettingsFile, value);
                _customLibrary = value;
                _customLibraryLoaded = true;
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
        /// (nunca sobrescreve o que o usuário editou).
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
    }
}
