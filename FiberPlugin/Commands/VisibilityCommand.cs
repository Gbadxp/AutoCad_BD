using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FiberPlugin.Commands
{
    public class VisibilityCommand
    {
        /// <summary>Objeto do projeto com o tipo, o cadastro e o que some junto com ele (texto, textos dos vãos, seta da reserva).</summary>
        private sealed class Owner
        {
            public ObjectId Id;
            public string Kind = "";
            public bool Done;
            public readonly List<ObjectId> Followers = new List<ObjectId>();
        }

        /// <summary>
        /// Janela Visibilidade: deixa à vista só os tipos marcados (postes, CTO, CEO, cabos, setas de esforço, outros) e, pelo
        /// cadastro na Energisa, todos, só os cadastrados ou só os que faltam; o resto fica oculto, na tela, nas folhas e na
        /// impressão. O texto de cada poste e caixa, os textos dos vãos de cada cabo e a seta de cada reserva somem junto.
        /// As combinações podem ser guardadas como presets.
        /// </summary>
        [CommandMethod("FIBRA_VISIBILIDADE")]
        public void SetVisibility()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<Owner> owners;
            List<(ObjectId Id, Owner? Pole)> arrows;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                (owners, arrows) = Collect(tr, CadHelpers.OpenModelSpace(tr, db, OpenMode.ForRead));
                tr.Commit();
            }
            var objects = owners.Select(o => (o.Kind, (bool?)o.Done))
                .Concat(arrows.Select(a => (VisibilityFilter.Arrows, a.Pole == null ? (bool?)null : a.Pole.Done))).ToList();

            VisibilityFilter filter;
            bool showAll;
            using (var form = new UI.VisibilityForm(objects, VisibilityFilter.Load(db), VisibilityPresets.Load()))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                filter = form.Result;
                showAll = form.ShowAll;
            }

            int hidden = 0, locked = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                void Set(ObjectId id, bool show)
                {
                    if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity ent) return;
                    if (!show) hidden++;
                    if (ent.Visible == show) return;
                    if (CadHelpers.IsOnLockedLayer(tr, id)) { locked++; return; }
                    ent.UpgradeOpen();
                    ent.Visible = show;
                }

                foreach (Owner owner in owners)
                {
                    bool show = filter.Shows(owner.Kind, owner.Done);
                    Set(owner.Id, show);
                    foreach (ObjectId follower in owner.Followers) Set(follower, show);
                }
                foreach (var (id, pole) in arrows) Set(id, filter.Shows(VisibilityFilter.Arrows, pole?.Done));

                filter.Save(tr, db);
                tr.Commit();
            }
            // As marcas do cadastro à vista acompanham (as mesmas cores): objeto oculto fica sem marca
            var (marksDone, marksPending) = RegistrationCommand.ShownMarks(db);
            if (marksDone || marksPending) RegistrationCommand.RedrawMarks(db, marksDone, marksPending);

            ed.WriteMessage(showAll || filter.ShowsEverything
                ? "\n[SUCESSO]: Tudo à vista de novo."
                : $"\n[SUCESSO]: Visibilidade: {filter.Summary}. {hidden} objeto(s) e texto(s) ocultos (não aparecem na tela, nas folhas " +
                  "nem na impressão). Para ver tudo: Visibilidade > Mostrar tudo.");
            if (locked > 0) ed.WriteMessage($"\n[AVISO]: {locked} objeto(s) em layer travada ficaram como estavam.");
            ed.Regen();
        }

        /// <summary>
        /// Objetos do projeto (os mesmos tipos do cadastro) com o que vai junto, e as setas de esforço com o poste delas.
        /// </summary>
        private static (List<Owner> Owners, List<(ObjectId Id, Owner? Pole)> Arrows) Collect(Transaction tr, BlockTableRecord space)
        {
            List<PoleInfo> poles = Poles.Collect(tr, space);
            var polesByHandle = Poles.ByHandle(poles);
            var owners = new List<Owner>();
            var byId = new Dictionary<ObjectId, Owner>();
            var cables = new List<(Owner Owner, Polyline Line)>();
            var strands = new List<(Owner Owner, Polyline Line)>();
            var reserves = new List<(Owner Owner, Point3d At)>();
            var cableTexts = new List<(ObjectId Id, string Layer, Point3d At)>();
            var strandTexts = new List<(ObjectId Id, Point3d At)>();
            var reserveArrows = new List<(ObjectId Id, Point3d At)>();
            var arrowPoles = new List<(ObjectId Id, PoleInfo? Pole)>();

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent || Registration.IsMark(ent)) continue;

                if (XDataTags.ReadEffortMarker(ent) is EffortMarkerData marker)
                {
                    arrowPoles.Add((id, EffortMarkers.Locate(ent, marker, poles, polesByHandle).Pole));
                    continue;
                }
                if (XDataTags.TryGetEffortPole(ent, out Point3d at))
                {
                    arrowPoles.Add((id, Poles.Nearest(poles, at, FiberSettings.PoleLinkRadius)));
                    continue;
                }

                if (Registration.KindOf(tr, ent) is string kind)
                {
                    var owner = new Owner { Id = id, Kind = kind, Done = Registration.IsDone(ent) };
                    owners.Add(owner);
                    byId[id] = owner;
                    if (ent is Polyline line)
                    {
                        if (kind == Registration.Cables) cables.Add((owner, line));
                        else strands.Add((owner, line));
                    }
                    else if (ent is MText reserve) reserves.Add((owner, reserve.Location));
                    continue;
                }

                if (ent is MText text)
                {
                    if (text.Layer.StartsWith(FiberSettings.CableLayerPrefix, StringComparison.OrdinalIgnoreCase) ||
                        text.Layer.StartsWith(FiberSettings.BackboneLayerPrefix, StringComparison.OrdinalIgnoreCase))
                        cableTexts.Add((id, text.Layer, Center(text)));
                    else if (XDataTags.IsStrand(text))
                        strandTexts.Add((id, Center(text)));
                }
                else if (ent is Polyline arrow && XDataTags.IsReserve(arrow) && arrow.NumberOfVertices > 0)
                {
                    reserveArrows.Add((id, arrow.GetPoint3dAt(0)));
                }
            }

            // Texto de cada poste e CTO/CEO (o mesmo levantamento das outras ferramentas, que acha o texto em desenho copiado)
            Dictionary<string, List<ObjectId>> labels = PoleLabels.IndexLabels(tr, space);
            foreach (Owner owner in owners.Where(o => o.Kind == Registration.Poles || o.Kind == Registration.Cto || o.Kind == Registration.Ceo))
            {
                if (labels.TryGetValue(owner.Id.Handle.ToString(), out List<ObjectId>? ids)) owner.Followers.AddRange(ids);
            }

            // Textos dos vãos: do cabo mais perto na mesma layer; da cordoalha, da cordoalha mais perto; seta da reserva, do
            // texto de reserva mais perto
            foreach (var (id, layer, point) in cableTexts)
            {
                Owner? cable = cables.Where(c => c.Line.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => Distance(c.Line, point)).Select(c => c.Owner).FirstOrDefault();
                cable?.Followers.Add(id);
            }
            foreach (var (id, point) in strandTexts)
                strands.OrderBy(s => Distance(s.Line, point)).Select(s => s.Owner).FirstOrDefault()?.Followers.Add(id);
            foreach (var (id, point) in reserveArrows)
                reserves.OrderBy(r => r.At.DistanceTo(point)).Select(r => r.Owner).FirstOrDefault()?.Followers.Add(id);

            var arrows = arrowPoles.Select(a => (a.Id, a.Pole != null && byId.TryGetValue(a.Pole.Id, out Owner? pole) ? pole : null)).ToList();
            return (owners, arrows);
        }

        private static double Distance(Polyline line, Point3d point)
        {
            try
            {
                return line.GetClosestPointTo(point, false).DistanceTo(point);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return double.MaxValue;
            }
        }

        private static Point3d Center(Entity ent)
        {
            try
            {
                Extents3d e = ent.GeometricExtents;
                return new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2, 0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return ent is MText t ? t.Location : Point3d.Origin;
            }
        }
    }
}
