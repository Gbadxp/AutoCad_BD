using System;
using System.IO;
using System.Reflection;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Localiza as pastas "Dados" (planilhas) e "Blocos" (biblioteca de blocos .dwg).
    ///
    /// Instalado (pacote .bundle em ApplicationPlugins): usa Documentos\Fiber Plugin\Dados e \Blocos,
    /// que o usuário pode editar à vontade. Na primeira execução essas pastas são criadas com o
    /// conteúdo padrão que vem no pacote.
    ///
    /// Desenvolvimento: procura ao lado da DLL e nas pastas acima dela, então rodando a partir de
    /// bin\Debug\... as pastas do código-fonte (FiberPlugin\Dados e FiberPlugin\Blocos) são usadas direto.
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

        public static string? DataDir => Resolve(DataFolderName);
        public static string? BlocksDir => Resolve(BlocksFolderName);

        /// <summary>Caminho de um arquivo dentro da pasta Dados (null se a pasta não existir).</summary>
        public static string? DataFile(string fileName)
        {
            string? dir = DataDir;
            return dir == null ? null : Path.Combine(dir, fileName);
        }

        /// <summary>
        /// Cria Documentos\Fiber Plugin\Dados e \Blocos com o conteúdo padrão do pacote. Cada pasta só é
        /// copiada quando ainda não existe, então nada que o usuário editou ou apagou é sobrescrito.
        /// Para voltar ao padrão, basta apagar a pasta e reabrir o AutoCAD.
        /// </summary>
        public static void EnsureUserFolders()
        {
            if (_userFoldersChecked || !IsInstalled) return;
            _userFoldersChecked = true;

            foreach (string folder in new[] { DataFolderName, BlocksFolderName })
            {
                string target = Path.Combine(UserRoot, folder);
                if (Directory.Exists(target)) continue;

                try
                {
                    Directory.CreateDirectory(target);
                    string? source = FindUpwards(folder); // Contents\Dados e Contents\Blocos dentro do .bundle
                    if (source != null) CopyDirectory(source, target);

                    // O instalador .msi não leva pastas vazias: garante as categorias padrão
                    if (folder == BlocksFolderName)
                    {
                        foreach (string category in BlockRepository.StandardCategories)
                            Directory.CreateDirectory(Path.Combine(target, category));
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string? Resolve(string folderName)
        {
            if (!IsInstalled) return FindUpwards(folderName);

            EnsureUserFolders();
            string dir = Path.Combine(UserRoot, folderName);
            return Directory.Exists(dir) ? dir : FindUpwards(folderName);
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

        private static void CopyDirectory(string source, string target)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target);
                if (!File.Exists(destination)) File.Copy(file, destination);
            }

            // Subpastas vazias (categorias ainda sem blocos)
            foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(target, relative));
            }
        }
    }
}
