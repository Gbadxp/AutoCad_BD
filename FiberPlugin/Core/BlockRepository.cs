using System.IO;
using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    public class BlockEntry
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = BlockCategories.Others;
    }

    /// <summary>
    /// Biblioteca de blocos: um único arquivo BLOCOS.dwg, com as definições de todos os blocos do projeto
    /// (o escolhido no Atualizar Blocos ou, sem ele, o da pasta Blocos).
    /// O plugin só oferece os blocos definidos na biblioteca. Quando um deles é usado e ainda não existe
    /// no desenho, a definição é copiada automaticamente, sem precisar de template.
    /// O grupo de cada bloco (e o comando que o insere) vem do nome: veja BlockCategories.
    /// </summary>
    public static class BlockRepository
    {
        public const string LibraryFileName = "BLOCOS.dwg";

        // Nomes dos blocos por arquivo, relidos só quando o arquivo muda
        private static readonly Dictionary<string, (DateTime Stamp, List<string> Names)> Cache =
            new Dictionary<string, (DateTime, List<string>)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Último problema ao ler a biblioteca (arquivo corrompido, sem permissão...), para mostrar ao usuário.</summary>
        public static string? LastError { get; private set; }

        /// <summary>
        /// Caminho do BLOCOS.dwg principal: o escolhido no Atualizar Blocos ou, sem ele, o da pasta Blocos
        /// (null se a pasta não existir).
        /// </summary>
        public static string? LibraryFile
        {
            get
            {
                if (PluginPaths.CustomLibrary is string custom && File.Exists(custom)) return custom;
                string? dir = PluginPaths.BlocksDir;
                return dir == null ? null : Path.Combine(dir, LibraryFileName);
            }
        }

        /// <summary>Todos os blocos da biblioteca, em ordem alfabética. Lista vazia (com LastError) se o arquivo não existir.</summary>
        public static List<BlockEntry> List()
        {
            LastError = null;
            string? file = LibraryFile;
            if (file == null || !File.Exists(file))
            {
                LastError = $"{LibraryFileName} não encontrado ({file ?? "pasta Blocos"}). Use Configurações > Projeto > Atualizar Blocos para escolher o arquivo.";
                return new List<BlockEntry>();
            }

            return BlockNamesIn(file)
                .Select(name => new BlockEntry { Name = name, Category = BlockCategories.Of(name) })
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Deixa no desenho a definição do bloco que está na biblioteca (BLOCOS.dwg). Se o desenho já tinha
        /// um bloco com esse nome (ex.: vindo de um template antigo), ele é substituído pela versão da
        /// biblioteca, e os blocos já inseridos passam a ter o desenho novo. Blocos que não estão na
        /// biblioteca (ex.: SETA DE ESFORÇO criada no próprio desenho) continuam os do desenho.
        /// Deve ser chamado fora de transações abertas. Retorna ObjectId.Null se não encontrado.
        /// </summary>
        public static ObjectId EnsureInDrawing(Database db, string blockName)
        {
            BlockEntry? entry = List().FirstOrDefault(e => e.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
            if (entry == null || LibraryFile is not string file)
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    return bt.Has(blockName) ? bt[blockName] : ObjectId.Null;
                }
            }

            return CopyDefinitions(db, file, new[] { entry.Name }).TryGetValue(entry.Name, out ObjectId id) ? id : ObjectId.Null;
        }

        /// <summary>
        /// Troca, no desenho, a definição de todos os blocos que também estão na biblioteca: os já inseridos
        /// passam a ter o desenho novo. Blocos da biblioteca que o desenho ainda não usa não são copiados.
        /// Retorna quantos blocos foram atualizados.
        /// </summary>
        public static int UpdateDrawing(Database db)
        {
            var inDrawing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (IsUserBlock(btr)) inDrawing.Add(btr.Name);
                }
            }

            List<string> names = List().Select(e => e.Name).Where(inDrawing.Contains).ToList();
            return names.Count == 0 || LibraryFile is not string file ? 0 : CopyDefinitions(db, file, names).Count;
        }

        /// <summary>Copia (substituindo) as definições dos blocos do arquivo para o desenho. Retorna nome → bloco no desenho.</summary>
        private static Dictionary<string, ObjectId> CopyDefinitions(Database db, string file, IEnumerable<string> names)
        {
            var copied = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            using (Database source = OpenLibrary(file))
            {
                var sourceIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                using (Transaction tr = source.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                    foreach (string name in names)
                    {
                        if (bt.Has(name)) sourceIds[name] = bt[name];
                    }
                }
                if (sourceIds.Count == 0) return copied;

                // Clonar a definição preserva blocos dinâmicos, atributos e blocos aninhados
                var mapping = new IdMapping();
                source.WblockCloneObjects(new ObjectIdCollection(sourceIds.Values.ToArray()), db.BlockTableId, mapping,
                    DuplicateRecordCloning.Replace, false);
                foreach (var pair in sourceIds) copied[pair.Key] = mapping[pair.Value].Value;
            }
            return copied;
        }

        /// <summary>
        /// Copia os blocos do desenho aberto para dentro do BLOCOS.dwg. Retorna (adicionados, substituídos,
        /// mantidos, erro). O arquivo antigo fica no histórico do Git.
        /// </summary>
        public static (int Added, int Replaced, int Kept, string? Error) ExportToLibrary(Database db, bool overwrite)
        {
            // O BLOCOS.dwg do pacote fica em Arquivos de Programas e não pode ser alterado
            if (PluginPaths.IsInstalled && PluginPaths.CustomLibrary == null)
            {
                return (0, 0, 0, $"Escolha o seu {LibraryFileName} em Configurações > Projeto > Atualizar Blocos antes de exportar.");
            }
            string? path = LibraryFile;
            if (path == null) return (0, 0, 0, $"{LibraryFileName} não encontrado. Use Configurações > Projeto > Atualizar Blocos para escolher o arquivo.");

            int added = 0, replaced = 0, kept = 0;
            var toCopy = new ObjectIdCollection();

            try
            {
                using (Database library = File.Exists(path) ? OpenLibrary(path) : new Database(true, true))
                {
                    var libraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (Transaction tr = library.TransactionManager.StartOpenCloseTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(library.BlockTableId, OpenMode.ForRead);
                        foreach (ObjectId id in bt)
                        {
                            libraryNames.Add(((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
                        }
                    }

                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        foreach (ObjectId id in bt)
                        {
                            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                            if (!IsUserBlock(btr)) continue;

                            bool exists = libraryNames.Contains(btr.Name);
                            if (exists && !overwrite) { kept++; continue; }

                            toCopy.Add(id);
                            if (exists) replaced++; else added++;
                        }
                    }

                    if (toCopy.Count == 0) return (0, 0, kept, null);

                    var mapping = new IdMapping();
                    db.WblockCloneObjects(toCopy, library.BlockTableId, mapping,
                        overwrite ? DuplicateRecordCloning.Replace : DuplicateRecordCloning.Ignore, false);

                    // Grava num arquivo temporário e só depois troca, para nunca corromper a biblioteca
                    string temp = Path.Combine(Path.GetDirectoryName(path) ?? ".", "~BLOCOS_tmp.dwg");
                    library.SaveAs(temp, DwgVersion.Current);

                    File.Copy(temp, path, true);
                    File.Delete(temp);
                }
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return (0, 0, kept, $"Não foi possível gravar {LibraryFileName} ({ex.Message}). Se ele estiver aberto no AutoCAD, feche-o e tente de novo.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                return (0, 0, kept, $"Erro ao copiar os blocos para {LibraryFileName}: {ex.Message}");
            }

            Cache.Remove(path);
            return (added, replaced, kept, null);
        }

        private static List<string> BlockNamesIn(string file)
        {
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(file);
                if (Cache.TryGetValue(file, out var cached) && cached.Stamp == stamp) return cached.Names;

                var names = new List<string>();
                using (Database source = OpenLibrary(file))
                using (Transaction tr = source.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in bt)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (IsUserBlock(btr)) names.Add(btr.Name);
                    }
                }

                Cache[file] = (stamp, names);
                return names;
            }
            catch (System.Exception ex)
            {
                LastError = $"Não foi possível ler {Path.GetFileName(file)}: {ex.Message}";
                return new List<string>();
            }
        }

        /// <summary>Abre um .dwg da biblioteca só para leitura (funciona mesmo com ele aberto no AutoCAD).</summary>
        private static Database OpenLibrary(string path)
        {
            var db = new Database(false, true);
            try
            {
                db.ReadDwgFile(path, FileShare.ReadWrite, true, "");
                db.CloseInput(true);
                return db;
            }
            catch
            {
                db.Dispose();
                throw;
            }
        }

        private static bool IsUserBlock(BlockTableRecord btr)
        {
            return !btr.IsLayout && !btr.IsAnonymous && !btr.IsFromExternalReference && !btr.IsDependent
                && !btr.Name.StartsWith("*")
                && !btr.Name.StartsWith("_")          // pontas de seta de cota (_ArchTick, _Oblique...)
                && !btr.Name.StartsWith("A$C");       // blocos temporários gerados por copiar/colar
        }
    }
}
