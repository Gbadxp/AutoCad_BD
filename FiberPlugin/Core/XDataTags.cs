using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Identifica as entidades do plugin via XData, em vez de deduzir tudo pelo nome da layer.
    /// Formato: [RegApp FIBRA_PLUGIN] [1000 tipo] [dados...]
    /// </summary>
    public static class XDataTags
    {
        private const string CableKind = "CABO";
        private const string EffortKind = "ESFORCO";

        /// <summary>Marca a polilinha como cabo do tipo informado (nome curto do catálogo).</summary>
        public static void TagCable(Transaction tr, Database db, Entity ent, string cableShortName)
        {
            CadHelpers.EnsureRegApp(tr, db);
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FiberSettings.AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, CableKind),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, cableShortName));
        }

        /// <summary>Marca a seta/texto de esforço com o ponto do poste a que ela se refere.</summary>
        public static void TagEffortMarker(Transaction tr, Database db, Entity ent, Point3d pole)
        {
            CadHelpers.EnsureRegApp(tr, db);
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FiberSettings.AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, EffortKind),
                new TypedValue((int)DxfCode.ExtendedDataXCoordinate, pole));
        }

        /// <summary>
        /// Nome curto do cabo desta entidade. Lê o XData; para desenhos feitos com versões
        /// anteriores do plugin, cai no nome da layer (FIBRA_CABO_ASU-80_06F.O → ASU-80 06F.O).
        /// </summary>
        public static string? GetCableName(Entity ent)
        {
            TypedValue[]? data = Read(ent);
            if (data != null && data.Length >= 3 && (data[1].Value as string) == CableKind)
            {
                return data[2].Value as string;
            }

            if (ent.Layer.StartsWith(FiberSettings.CableLayerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ent.Layer.Substring(FiberSettings.CableLayerPrefix.Length).Replace("_", " ");
            }

            return null;
        }

        public static bool TryGetEffortPole(Entity ent, out Point3d pole)
        {
            TypedValue[]? data = Read(ent);
            if (data != null && data.Length >= 3 && (data[1].Value as string) == EffortKind && data[2].Value is Point3d p)
            {
                pole = p;
                return true;
            }
            pole = Point3d.Origin;
            return false;
        }

        private static TypedValue[]? Read(Entity ent)
        {
            using (ResultBuffer? rb = ent.GetXDataForApplication(FiberSettings.AppName))
            {
                return rb?.AsArray();
            }
        }
    }
}
