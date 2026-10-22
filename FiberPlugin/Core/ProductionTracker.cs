using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using FiberPlugin.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Anota no arquivo de produção (ProductionLog) o que cada desenho tem no começo do dia (ao abrir, ou no primeiro
    /// comando do dia se ele ficou aberto de um dia para o outro) e a cada vez que é salvo. A produção do dia de um
    /// desenho é o que ele tem no fim (salvo, ou agora se ainda está aberto) menos o que tinha no começo.
    /// Nada aqui pode atrapalhar o AutoCAD: qualquer erro só deixa de anotar.
    /// </summary>
    public static class ProductionTracker
    {
        private sealed class Tracked
        {
            public string? File;            // Null enquanto o desenho não foi salvo (Desenho1.dwg)
            public DateTime Day;            // Dia do começo já anotado
            public ProductionCount? Start;  // Começo do dia do desenho ainda sem arquivo
        }

        private static readonly Dictionary<Database, Tracked> Docs = new Dictionary<Database, Tracked>();
        private static bool _started;

        /// <summary>Começa a acompanhar os desenhos abertos e os que forem abertos depois.</summary>
        public static void Start()
        {
            if (_started) return;
            _started = true;
            DocumentCollection documents = AcApp.DocumentManager;
            documents.DocumentCreated += (s, e) => Attach(e.Document);
            documents.DocumentToBeDestroyed += (s, e) => Detach(e.Document);
            foreach (Document doc in documents) Attach(doc);
        }

        private static void Attach(Document? doc)
        {
            if (doc == null || Docs.ContainsKey(doc.Database)) return;
            Docs[doc.Database] = new Tracked();
            doc.Database.SaveComplete += OnSaved;
            doc.CommandWillStart += (s, e) => StartDay(doc);
            StartDay(doc);
        }

        private static void Detach(Document? doc)
        {
            if (doc == null || !Docs.Remove(doc.Database)) return;
            doc.Database.SaveComplete -= OnSaved;
        }

        /// <summary>Anota o começo do dia do desenho, se ainda não anotou hoje.</summary>
        private static void StartDay(Document doc)
        {
            try
            {
                if (!Docs.TryGetValue(doc.Database, out Tracked? t) || t.Day == DateTime.Today) return;
                t.Day = DateTime.Today;
                t.File = FileOf(doc);
                ProductionCount now = Count(doc.Database);
                if (t.File == null)
                {
                    t.Start = now;
                    return;
                }
                t.Start = null;
                ProductionLog log = ProductionLog.Default;
                if (log.Last(t.Day, t.File) == null) log.Append(DateTime.Now, ProductionEntry.StartEvent, t.File, now);
            }
            catch (System.Exception)
            {
                // Sem anotar o começo: a produção do dia desse desenho começa na primeira vez que ele for salvo
            }
        }

        private static void OnSaved(object? sender, DatabaseIOEventArgs e)
        {
            try
            {
                if (sender is not Database db || !Docs.TryGetValue(db, out Tracked? t)) return;
                string file = e.FileName ?? "";
                // Salvamento automático (.sv$), DXF e outros: só o desenho de verdade
                if (!file.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) return;

                DateTime today = DateTime.Today;
                ProductionCount now = Count(db);
                ProductionLog log = ProductionLog.Default;
                if (log.Last(today, file) == null)
                {
                    // Desenho salvo hoje pela primeira vez com este nome (Salvar como, ou desenho novo): o começo do dia
                    // dele é o do desenho de antes, até o ponto em que o de antes foi salvo
                    ProductionCount start = t.Day != today ? now
                        : t.File != null ? log.Last(today, t.File)?.Count ?? now
                        : t.Start ?? now;
                    log.Append(DateTime.Now, ProductionEntry.StartEvent, file, start);
                }
                log.Append(DateTime.Now, ProductionEntry.SavedEvent, file, now);
                t.File = file;
                t.Day = today;
                t.Start = null;
            }
            catch (System.Exception)
            {
                // Sem anotar: vale a anotação anterior
            }
        }

        /// <summary>Arquivo do desenho; null se ainda não foi salvo nenhuma vez.</summary>
        private static string? FileOf(Document doc)
        {
            if (!doc.IsNamedDrawing) return null;
            string file = doc.Database.Filename;
            return string.IsNullOrEmpty(file) ? null : file;
        }

        /// <summary>Desenhos abertos agora, com o que têm (a produção de hoje conta também o que ainda não foi salvo).</summary>
        public static List<LiveDrawing> OpenDrawings()
        {
            var drawings = new List<LiveDrawing>();
            foreach (Document doc in AcApp.DocumentManager)
            {
                try
                {
                    StartDay(doc);
                    Docs.TryGetValue(doc.Database, out Tracked? t);
                    drawings.Add(new LiveDrawing
                    {
                        File = t?.File ?? FileOf(doc),
                        Name = Path.GetFileName(doc.Name),
                        Now = Count(doc.Database),
                        Start = t?.Start
                    });
                }
                catch (System.Exception)
                {
                    // Desenho que não deu para ler agora: vale o que foi anotado ao salvar
                }
            }
            return drawings;
        }

        /// <summary>O que o desenho tem agora no Model: cabos, CTO, CEO, postes e reservas técnicas.</summary>
        public static ProductionCount Count(Database db)
        {
            var count = new ProductionCount();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                BlockTableRecord modelSpace = CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead);
                foreach (CableTotal cable in CableDrawing.Totals(tr, modelSpace, new List<CableModel>()))
                    count.CableMeters[cable.Name] = cable.Length;
                foreach (BoxInfo box in Boxes.Collect(tr, modelSpace))
                {
                    if (box.Data.Kind == BlockCategories.Cto) count.Cto++;
                    else if (box.Data.Kind == BlockCategories.Ceo) count.Ceo++;
                }
                count.Poles = Poles.Collect(tr, modelSpace).Count;

                foreach (ObjectId id in modelSpace)
                {
                    if (ReserveText(tr, tr.GetObject(id, OpenMode.ForRead)) is string text &&
                        ProductionCount.IsReserve(text, out double meters))
                    {
                        count.Reserves++;
                        count.ReserveMeters += meters;
                    }
                }
                tr.Commit();
            }
            return count;
        }

        /// <summary>
        /// Texto em que pode estar escrita uma reserva técnica: o texto (MText ou Text) ou, num bloco, o nome dele junto
        /// com os atributos. Null nos outros desenhos.
        /// </summary>
        private static string? ReserveText(Transaction tr, DBObject obj)
        {
            switch (obj)
            {
                case MText mtext: return mtext.Text;
                case DBText text: return text.TextString;
                case BlockReference br:
                    string attributes = string.Join(" ", br.AttributeCollection.Cast<ObjectId>()
                        .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<AttributeReference>().Select(a => a.TextString));
                    return CadHelpers.GetBlockName(tr, br) + " " + attributes;
                default: return null;
            }
        }
    }
}
