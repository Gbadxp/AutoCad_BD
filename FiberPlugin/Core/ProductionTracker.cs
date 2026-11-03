using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Anota no arquivo de produção (ProductionLog) o começo do dia de cada desenho (ao abrir, ou no primeiro comando do
    /// dia se ele ficou aberto de um dia para o outro) e, a cada vez que é salvo, o que foi desenhado nele desde então.
    /// O começo é o handle que o AutoCAD ia dar ao próximo objeto (Database.Handseed): os handles só crescem, então o que
    /// tem handle a partir dele foi criado no dia. O que já estava e foi apagado não conta (não fica negativo).
    /// Nada aqui pode atrapalhar o AutoCAD: qualquer erro só deixa de anotar.
    /// </summary>
    public static class ProductionTracker
    {
        private sealed class Tracked
        {
            public string? File;            // Null enquanto o desenho não foi salvo (Desenho1.dwg)
            public DateTime Day;            // Dia do começo já anotado
            public long Since;              // Começo do dia: os objetos com handle a partir deste foram criados no dia
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
                ProductionLog log = ProductionLog.Default;
                // Reaberto no mesmo dia: continua do começo anotado (os handles ficam gravados no arquivo)
                if (t.File != null && log.Last(t.Day, t.File)?.Since is long since)
                {
                    t.Since = since;
                    return;
                }
                t.Since = doc.Database.Handseed.Value;
                if (t.File != null) log.Append(DateTime.Now, ProductionEntry.StartEvent, t.File, new ProductionCount(), t.Since);
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
                if (t.Day != today)
                {
                    // Sem começo anotado hoje: começa agora
                    t.Day = today;
                    t.Since = db.Handseed.Value;
                }
                ProductionCount now = Count(db, t.Since);
                ProductionLog log = ProductionLog.Default;
                if (log.Last(today, file)?.Since != t.Since)
                {
                    // Desenho salvo hoje pela primeira vez com este nome (Salvar como, ou desenho novo): começa do que o
                    // desenho de antes já tinha anotado hoje, para não contar duas vezes
                    ProductionCount start = t.File != null && !t.File.Equals(file, StringComparison.OrdinalIgnoreCase) &&
                                            log.Last(today, t.File) is ProductionEntry before && before.Since == t.Since
                        ? before.Count
                        : new ProductionCount();
                    log.Append(DateTime.Now, ProductionEntry.StartEvent, file, start, t.Since);
                }
                log.Append(DateTime.Now, ProductionEntry.SavedEvent, file, now, t.Since);
                t.File = file;
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
                        Now = Count(doc.Database, t?.Since ?? 0),
                        Since = t?.Since
                    });
                }
                catch (System.Exception)
                {
                    // Desenho que não deu para ler agora: vale o que foi anotado ao salvar
                }
            }
            return drawings;
        }

        /// <summary>
        /// O que o desenho tem agora no Model: cabos, CTO, CEO, postes, amarrações, reservas técnicas e cordoalha, só dos
        /// objetos com handle a partir de <paramref name="since"/> (0 = todos). Uma passada só (roda a cada vez que o desenho
        /// é salvo), com as regras do relatório: cabo pelo nome (CableDrawing.Totals), CTO/CEO pelo XData (Boxes.Collect) e
        /// postes como no Poles.Collect.
        /// </summary>
        public static ProductionCount Count(Database db, long since = 0)
        {
            var count = new ProductionCount();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead))
                {
                    if (id.Handle.Value < since) continue; // Já estava no começo do dia
                    switch (tr.GetObject(id, OpenMode.ForRead))
                    {
                        case Polyline poly when XDataTags.GetCableName(poly) is string cable:
                            count.CableMeters[cable] = (count.CableMeters.TryGetValue(cable, out double meters) ? meters : 0) + poly.Length;
                            break;
                        case Polyline poly when Strand.IsStrand(poly):
                            count.StrandMeters += poly.Length;
                            break;
                        case BlockReference br:
                            string? kind = XDataTags.ReadBox(br)?.Kind;
                            if (kind == BlockCategories.Cto) count.Cto++;
                            else if (kind == BlockCategories.Ceo) count.Ceo++;
                            bool pole = Poles.Read(tr, id, br) != null;
                            if (pole) count.Poles++;
                            if (kind != null || pole) break;
                            string blockName = CadHelpers.GetBlockName(tr, br);
                            if (BlockCategories.Of(blockName) == BlockCategories.Anchoring)
                            {
                                count.Anchorings++; // Amarração (ancoragem), como no relatório
                                break;
                            }
                            // Reserva em bloco: o nome dele ou um atributo
                            AddReserve(count, blockName + " " + string.Join(" ",
                                br.AttributeCollection.Cast<ObjectId>().Select(a => tr.GetObject(a, OpenMode.ForRead))
                                    .OfType<AttributeReference>().Select(a => a.TextString)));
                            break;
                        case MText mtext:
                            AddReserve(count, mtext.Text);
                            break;
                        case DBText text:
                            AddReserve(count, text.TextString);
                            break;
                    }
                }
                tr.Commit();
            }
            return count;
        }

        private static void AddReserve(ProductionCount count, string text)
        {
            if (!ProductionCount.IsReserve(text, out double meters)) return;
            count.Reserves++;
            count.ReserveMeters += meters;
        }
    }
}
