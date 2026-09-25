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
        private const string SheetIndexKind = "FOLHA";
        private const string PoleKind = "POSTE";
        private const string PoleLabelKind = "ROTULO_POSTE";

        /// <summary>Grava no bloco os dados do poste: número, tipo (DT/CC), altura e esforço nominal.</summary>
        public static void TagPole(Transaction tr, Database db, Entity ent, PoleData data)
        {
            CadHelpers.EnsureRegApp(tr, db);
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FiberSettings.AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, PoleKind),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, data.Number),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, data.Type),
                new TypedValue((int)DxfCode.ExtendedDataReal, data.HeightM),
                new TypedValue((int)DxfCode.ExtendedDataReal, data.EffortDaN));
        }

        public static PoleData? ReadPole(Entity ent)
        {
            TypedValue[]? d = Read(ent);
            if (d == null || d.Length < 6 || (d[1].Value as string) != PoleKind) return null;
            if (d[2].Value is not int number || d[3].Value is not string type ||
                d[4].Value is not double height || d[5].Value is not double effort) return null;

            return new PoleData { Number = number, Type = type, HeightM = height, EffortDaN = effort };
        }

        /// <summary>Marca o texto de identificação com o handle do bloco do poste a que ele pertence.</summary>
        public static void TagPoleLabel(Transaction tr, Database db, Entity ent, string poleHandle)
        {
            CadHelpers.EnsureRegApp(tr, db);
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FiberSettings.AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, PoleLabelKind),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, poleHandle));
        }

        public static string? GetPoleLabelOwner(Entity ent)
        {
            TypedValue[]? d = Read(ent);
            if (d != null && d.Length >= 3 && (d[1].Value as string) == PoleLabelKind) return d[2].Value as string;
            return null;
        }

        /// <summary>Marca o retângulo/nome de uma folha no quadro de articulação (Model), com o prefixo das folhas.</summary>
        public static void TagSheetIndex(Transaction tr, Database db, Entity ent, string prefix)
        {
            CadHelpers.EnsureRegApp(tr, db);
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FiberSettings.AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, SheetIndexKind),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, prefix));
        }

        public static string? GetSheetIndexPrefix(Entity ent)
        {
            TypedValue[]? data = Read(ent);
            if (data != null && data.Length >= 3 && (data[1].Value as string) == SheetIndexKind) return data[2].Value as string;
            return null;
        }

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
