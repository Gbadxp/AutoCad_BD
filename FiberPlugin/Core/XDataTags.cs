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
        private const string BoxKind = "CAIXA";
        private const string CheckKind = "VERIFICACAO";

        private static TypedValue Text(string value) => new TypedValue((int)DxfCode.ExtendedDataAsciiString, value);
        private static TypedValue Int(int value) => new TypedValue((int)DxfCode.ExtendedDataInteger32, value);
        private static TypedValue Real(double value) => new TypedValue((int)DxfCode.ExtendedDataReal, value);

        /// <summary>Grava na CTO/CEO o tipo, o número e o poste vinculado.</summary>
        public static void TagBox(Transaction tr, Database db, Entity ent, BoxData data) =>
            Write(tr, db, ent, BoxKind, Text(data.Kind), Int(data.Number), Text(data.PoleHandle));

        public static BoxData? ReadBox(Entity ent)
        {
            TypedValue[]? d = Read(ent, BoxKind, 3);
            if (d == null || d[0].Value is not string kind || d[1].Value is not int number) return null;
            return new BoxData { Kind = kind, Number = number, PoleHandle = d[2].Value as string ?? "" };
        }

        /// <summary>Grava no bloco os dados do poste: número, tipo (DT/CC), altura, esforço nominal e esforço existente.</summary>
        public static void TagPole(Transaction tr, Database db, Entity ent, PoleData data) =>
            Write(tr, db, ent, PoleKind, Int(data.Number), Text(data.Type), Real(data.HeightM), Real(data.EffortDaN), Real(data.ExistingKgf),
                Text(data.EnergisaId));

        public static PoleData? ReadPole(Entity ent)
        {
            TypedValue[]? d = Read(ent, PoleKind, 4);
            if (d == null || d[0].Value is not int number || d[1].Value is not string type ||
                d[2].Value is not double height || d[3].Value is not double effort) return null;

            // Esforço existente e ID da Energisa vieram depois (1.9.15): postes antigos não têm
            return new PoleData
            {
                Number = number,
                Type = type,
                HeightM = height,
                EffortDaN = effort,
                ExistingKgf = d.Length > 4 && d[4].Value is double existing ? existing : 0,
                EnergisaId = d.Length > 5 ? d[5].Value as string ?? "" : ""
            };
        }

        /// <summary>
        /// Marca o texto de identificação com o handle do bloco (poste, CTO ou CEO) a que ele pertence. Gravado como handle
        /// do AutoCAD (código 1005), que o COPY, o copiar e colar e o INSERT traduzem junto: copiando poste e texto, a cópia
        /// do texto aponta para a cópia do poste. Até a 1.9.46 ia como texto puro (1000), que a cópia não acompanha.
        /// </summary>
        public static void TagPoleLabel(Transaction tr, Database db, Entity ent, string ownerHandle) =>
            Write(tr, db, ent, PoleLabelKind, new TypedValue((int)DxfCode.ExtendedDataHandle, ownerHandle));

        /// <summary>Handle do bloco dono do texto (dos dois jeitos de gravar). Null se não for texto de identificação.</summary>
        public static string? GetPoleLabelOwner(Entity ent) => GetPoleLabelOwner(ent, out _);

        /// <param name="isHandle">True se gravado como handle do AutoCAD (1.9.47 em diante), que o COPY e o colar mantêm certo.</param>
        public static string? GetPoleLabelOwner(Entity ent, out bool isHandle)
        {
            TypedValue[]? d = Read(ent, PoleLabelKind, 1);
            isHandle = d != null && d[0].TypeCode == (short)DxfCode.ExtendedDataHandle;
            string? owner = d?[0].Value?.ToString();
            if (owner == "0") { owner = ""; isHandle = false; } // O AUDIT zera handle que não existe mais
            return owner;
        }

        /// <summary>Marca o retângulo/nome de uma folha no quadro de articulação (Model), com o prefixo das folhas.</summary>
        public static void TagSheetIndex(Transaction tr, Database db, Entity ent, string prefix) =>
            Write(tr, db, ent, SheetIndexKind, Text(prefix));

        public static string? GetSheetIndexPrefix(Entity ent) => Read(ent, SheetIndexKind, 1)?[0].Value as string;

        /// <summary>Marca o círculo/texto de uma não conformidade do Verificar Projeto (apagados a cada verificação).</summary>
        public static void TagCheck(Transaction tr, Database db, Entity ent) => Write(tr, db, ent, CheckKind);

        public static bool IsCheck(Entity ent) => Read(ent, CheckKind, 0) != null;

        /// <summary>Marca a polilinha como cabo do tipo informado (nome curto do catálogo).</summary>
        public static void TagCable(Transaction tr, Database db, Entity ent, string cableShortName) =>
            Write(tr, db, ent, CableKind, Text(cableShortName));

        /// <summary>Marca a seta/texto de esforço com os dados do cálculo (lidos pelo relatório).</summary>
        public static void TagEffortMarker(Transaction tr, Database db, Entity ent, EffortMarkerData data) =>
            Write(tr, db, ent, EffortKind,
                new TypedValue((int)DxfCode.ExtendedDataXCoordinate, data.Point),
                Real(data.Kgf), Real(data.AngleDeg), Text(data.Situation), Text(data.PoleHandle), Int(data.CableCount),
                Real(data.TopKgf ?? -1));

        /// <summary>Dados completos da seta de esforço. Null em setas de versões antigas (só com o ponto).</summary>
        public static EffortMarkerData? ReadEffortMarker(Entity ent)
        {
            TypedValue[]? d = Read(ent, EffortKind, 6);
            if (d == null || d[0].Value is not Point3d point || d[1].Value is not double kgf || d[2].Value is not double angle ||
                d[5].Value is not int cables) return null;

            return new EffortMarkerData
            {
                Point = point,
                Kgf = kgf,
                AngleDeg = angle,
                Situation = d[3].Value as string ?? "",
                PoleHandle = d[4].Value as string ?? "",
                CableCount = cables,
                TopKgf = d.Length > 6 && d[6].Value is double top && top >= 0 ? top : (double?)null
            };
        }

        /// <summary>Ponto do poste de qualquer seta de esforço (também as de versões antigas).</summary>
        public static bool TryGetEffortPole(Entity ent, out Point3d pole)
        {
            object? value = Read(ent, EffortKind, 1)?[0].Value;
            pole = value is Point3d p ? p : Point3d.Origin;
            return value is Point3d;
        }

        /// <summary>
        /// Nome curto do cabo desta entidade. Lê o XData; para desenhos feitos com versões
        /// anteriores do plugin, cai no nome da layer (FIBRA_CABO_ASU-80_06F.O → ASU-80 06F.O).
        /// </summary>
        public static string? GetCableName(Entity ent)
        {
            if (Read(ent, CableKind, 1)?[0].Value is string name) return name;

            if (ent.Layer.StartsWith(FiberSettings.CableLayerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ent.Layer.Substring(FiberSettings.CableLayerPrefix.Length).Replace("_", " ");
            }
            return null;
        }

        private static void Write(Transaction tr, Database db, Entity ent, string kind, params TypedValue[] data)
        {
            CadHelpers.EnsureRegApp(tr, db);
            var values = new TypedValue[data.Length + 2];
            values[0] = new TypedValue((int)DxfCode.ExtendedDataRegAppName, FiberSettings.AppName);
            values[1] = Text(kind);
            data.CopyTo(values, 2);
            ent.XData = new ResultBuffer(values);
        }

        /// <summary>Dados depois do tipo, se a entidade for do tipo pedido e tiver pelo menos <paramref name="count"/> valores.</summary>
        private static TypedValue[]? Read(Entity ent, string kind, int count)
        {
            using (ResultBuffer? rb = ent.GetXDataForApplication(FiberSettings.AppName))
            {
                TypedValue[]? d = rb?.AsArray();
                if (d == null || d.Length < count + 2 || (d[1].Value as string) != kind) return null;

                var data = new TypedValue[d.Length - 2];
                Array.Copy(d, 2, data, 0, data.Length);
                return data;
            }
        }
    }
}
