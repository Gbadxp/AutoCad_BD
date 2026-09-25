using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace FiberPlugin.Core
{
    public class BlockEntry
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = BlockRepository.DefaultCategory;
        public string FilePath { get; set; } = "";   // Arquivo da biblioteca que contém a definição do bloco
    }

    /// <summary>
    /// Biblioteca de blocos: o arquivo Blocos\BLOCOS.dwg, com as definições de todos os blocos do projeto
    /// (outros .dwg soltos na pasta Blocos também são lidos, com prioridade para o BLOCOS.dwg).
    /// O plugin só oferece os blocos definidos na biblioteca. Quando um deles é usado e ainda não existe
    /// no desenho, a definição é copiada automaticamente, sem precisar de template.
    /// A categoria mostrada na janela de inserção vem do nome do bloco (POSTE, CTO, TRAFO...).
    /// </summary>
    public static class BlockRepository
    {
        public const string DefaultCategory = "Outros";
        public const string LibraryFileName = "BLOCOS.dwg";
        public static readonly string[] StandardCategories = { "Fibra", "Eletrica", "Postes", DefaultCategory };

        // Nomes dos blocos por arquivo, relidos só quando o arquivo muda
        private static readonly Dictionary<string, (DateTime Stamp, List<string> Names)> Cache =
            new Dictionary<string, (DateTime, List<string>)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Último problema ao ler a biblioteca (arquivo corrompido, sem permissão...), para mostrar ao usuário.</summary>
        public static string? LastError { get; private set; }

        /// <summary>Caminho do BLOCOS.dwg (null se a pasta Blocos não existir).</summary>
        public static string? LibraryFile
        {
            get
            {
                string? dir = PluginPaths.BlocksDir;
                return dir == null ? null : Path.Combine(dir, LibraryFileName);
            }
        }

        /// <summary>Todos os blocos da biblioteca, em ordem alfabética.</summary>
        public static List<BlockEntry> List()
        {
            LastError = null;
            var entries = new List<BlockEntry>();

            // Pastas em ordem de prioridade (blocos pessoais antes dos do pacote) e, em cada pasta,
            // o BLOCOS.dwg antes dos demais .dwg. Em nomes repetidos vale o primeiro encontrado.
            IEnumerable<string> files = PluginPaths.BlockLibraryDirs.SelectMany(dir =>
                Directory.EnumerateFiles(dir, "*.dwg", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("~"))
                    .OrderBy(f => !Path.GetFileName(f).Equals(LibraryFileName, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase));

            foreach (string file in files)
            {
                foreach (string name in BlockNamesIn(file))
                {
                    if (entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    entries.Add(new BlockEntry { Name = name, Category = GuessCategory(name), FilePath = file });
                }
            }

            return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Garante que o bloco existe no desenho, copiando a definição da biblioteca se necessário.
        /// Se o bloco já existe no desenho, a versão do desenho é mantida.
        /// Deve ser chamado fora de transações abertas. Retorna ObjectId.Null se não encontrado.
        /// </summary>
        public static ObjectId EnsureInDrawing(Database db, string blockName)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName)) return bt[blockName];
            }

            BlockEntry? entry = List().FirstOrDefault(e => e.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return ObjectId.Null;

            using (Database source = OpenLibrary(entry.FilePath))
            {
                ObjectId sourceId;
                using (Transaction tr = source.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                    if (!bt.Has(entry.Name)) return ObjectId.Null;
                    sourceId = bt[entry.Name];
                }

                // Clonar a definição preserva blocos dinâmicos, atributos e blocos aninhados
                var ids = new ObjectIdCollection { sourceId };
                var mapping = new IdMapping();
                source.WblockCloneObjects(ids, db.BlockTableId, mapping, DuplicateRecordCloning.Ignore, false);
                return mapping[sourceId].Value;
            }
        }

        /// <summary>
        /// Copia os blocos do desenho aberto para dentro do BLOCOS.dwg. Antes de gravar, o arquivo
        /// anterior é guardado como BLOCOS.bak. Retorna (adicionados, substituídos, mantidos, erro).
        /// </summary>
        public static (int Added, int Replaced, int Kept, string? Error) ExportToLibrary(Database db, bool overwrite)
        {
            string? path = LibraryFile;
            if (path == null)
            {
                string dir = Path.Combine(PluginPaths.AssemblyDir, PluginPaths.BlocksFolderName);
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, LibraryFileName);
            }

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

                    if (File.Exists(path)) File.Copy(path, Path.ChangeExtension(path, ".bak"), true);
                    File.Copy(temp, path, true);
                    File.Delete(temp);
                }
            }
            catch (IOException ex)
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

        public static string GuessCategory(string blockName)
        {
            string n = blockName.ToLowerInvariant();
            if (n.Contains("poste")) return "Postes";

            string[] fibra = { "cto", "ceo", "amarra", "reserva", "emenda", "splitter", "fibra", "dio", "seta de esfor" };
            if (fibra.Any(n.Contains)) return "Fibra";

            string[] eletrica = { "aterramento", "chave", "para-raio", "pára-raio", "trafo", "transformador", "luminaria", "luminária" };
            if (eletrica.Any(n.Contains)) return "Eletrica";

            return DefaultCategory;
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
