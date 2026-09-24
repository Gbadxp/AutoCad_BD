using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    public class BlockEntry
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = BlockRepository.DefaultCategory;
        public string? FilePath { get; set; }   // null = bloco que só existe no desenho
    }

    /// <summary>
    /// Biblioteca de blocos da pasta "Blocos": cada arquivo .dwg é um bloco, com o nome do arquivo.
    /// A subpasta define a categoria (Blocos\Fibra\CTO.dwg → categoria "Fibra").
    /// Quando um bloco é usado e não existe no desenho, ele é importado automaticamente, dispensando o template.
    /// </summary>
    public static class BlockRepository
    {
        public const string DefaultCategory = "Outros";
        public static readonly string[] StandardCategories = { "Fibra", "Eletrica", "Postes", DefaultCategory };

        /// <summary>Todos os blocos da pasta Blocos (vazio se a pasta não existir).</summary>
        public static List<BlockEntry> List()
        {
            string? root = PluginPaths.BlocksDir;
            var entries = new List<BlockEntry>();
            if (root == null) return entries;

            foreach (string file in Directory.EnumerateFiles(root, "*.dwg", SearchOption.AllDirectories))
            {
                // Caminho relativo à pasta Blocos (Path.GetRelativePath não existe no .NET Framework)
                string relativePath = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string relativeDir = Path.GetDirectoryName(relativePath) ?? "";
                string category = relativeDir.Length == 0
                    ? DefaultCategory
                    : relativeDir.Split(Path.DirectorySeparatorChar)[0];

                string name = Path.GetFileNameWithoutExtension(file);
                if (entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                entries.Add(new BlockEntry { Name = name, Category = category, FilePath = file });
            }
            return entries;
        }

        /// <summary>Blocos do desenho que podem ser inseridos pelo usuário (sem layouts, anônimos, xrefs...).</summary>
        public static List<string> DrawingBlockNames(Database db)
        {
            var names = new List<string>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (IsUserBlock(btr)) names.Add(btr.Name);
                }
                tr.Commit();
            }
            return names;
        }

        /// <summary>Junta biblioteca + desenho. Blocos só do desenho recebem categoria por palavra-chave.</summary>
        public static List<BlockEntry> ListAll(Database db)
        {
            List<BlockEntry> entries = List();
            foreach (string name in DrawingBlockNames(db))
            {
                if (entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                entries.Add(new BlockEntry { Name = name, Category = GuessCategory(name) });
            }
            return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Garante que o bloco existe no desenho, importando da biblioteca se necessário.
        /// Se o bloco já existe no desenho, a versão do desenho é mantida.
        /// Deve ser chamado fora de transações abertas. Retorna ObjectId.Null se não encontrado.
        /// </summary>
        public static ObjectId EnsureInDrawing(Database db, string blockName)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName))
                {
                    ObjectId id = bt[blockName];
                    tr.Commit();
                    return id;
                }
                tr.Commit();
            }

            BlockEntry? entry = List().FirstOrDefault(e => e.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
            if (entry?.FilePath == null) return ObjectId.Null;

            return Import(db, entry.Name, entry.FilePath);
        }

        private static ObjectId Import(Database db, string blockName, string filePath)
        {
            using (var source = new Database(false, true))
            {
                source.ReadDwgFile(filePath, FileShare.Read, true, "");
                source.CloseInput(true);

                // 1º caso: o arquivo contém a definição com o mesmo nome (gerado pelo FIBRA_EXPORTAR_BLOCOS).
                // Clonar a definição preserva blocos dinâmicos e atributos.
                ObjectId sourceId = ObjectId.Null;
                using (Transaction tr = source.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                    if (bt.Has(blockName)) sourceId = bt[blockName];
                    tr.Commit();
                }

                if (!sourceId.IsNull)
                {
                    var ids = new ObjectIdCollection { sourceId };
                    var mapping = new IdMapping();
                    source.WblockCloneObjects(ids, db.BlockTableId, mapping, DuplicateRecordCloning.Ignore, false);
                    return mapping[sourceId].Value;
                }

                // 2º caso: arquivo feito com WBLOCK comum → o conteúdo do Model Space vira o bloco
                return db.Insert(blockName, source, true);
            }
        }

        /// <summary>
        /// Exporta os blocos do desenho para a pasta Blocos (um .dwg por bloco, em subpasta por categoria).
        /// Retorna (exportados, pulados porque já existiam).
        /// </summary>
        public static (int Exported, int Skipped, List<string> Errors) ExportFromDrawing(Database db, string targetRoot, bool overwrite)
        {
            int exported = 0, skipped = 0;
            var errors = new List<string>();
            var blocks = new List<(ObjectId Id, string Name)>();
            Directory.CreateDirectory(targetRoot);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (IsUserBlock(btr)) blocks.Add((id, btr.Name));
                }
                tr.Commit();
            }

            foreach (var (id, name) in blocks)
            {
                string fileName = CadHelpers.SanitizeName(name) + ".dwg";
                string dir = Path.Combine(targetRoot, GuessCategory(name));
                string path = Path.Combine(dir, fileName);

                // Já existe na biblioteca (em qualquer categoria)?
                bool exists = Directory.EnumerateFiles(targetRoot, fileName, SearchOption.AllDirectories).Any();
                if (exists && !overwrite)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(dir);
                    using (var target = new Database(true, true))
                    {
                        var ids = new ObjectIdCollection { id };
                        var mapping = new IdMapping();
                        db.WblockCloneObjects(ids, target.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);

                        // Uma referência na origem, para o bloco aparecer quando o arquivo for aberto
                        using (Transaction ttr = target.TransactionManager.StartTransaction())
                        {
                            var tbt = (BlockTable)ttr.GetObject(target.BlockTableId, OpenMode.ForRead);
                            var ms = (BlockTableRecord)ttr.GetObject(tbt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                            var br = new BlockReference(Point3d.Origin, mapping[id].Value);
                            ms.AppendEntity(br);
                            ttr.AddNewlyCreatedDBObject(br, true);
                            ttr.Commit();
                        }

                        target.SaveAs(path, DwgVersion.Current);
                    }
                    exported++;
                }
                catch (System.Exception ex)
                {
                    errors.Add($"{name}: {ex.Message}");
                }
            }

            return (exported, skipped, errors);
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

        private static bool IsUserBlock(BlockTableRecord btr)
        {
            return !btr.IsLayout && !btr.IsAnonymous && !btr.IsFromExternalReference && !btr.IsDependent
                && !btr.Name.StartsWith("*")
                && !btr.Name.StartsWith("_")          // pontas de seta de cota (_ArchTick, _Oblique...)
                && !btr.Name.StartsWith("A$C");       // blocos temporários gerados por copiar/colar
        }
    }
}
