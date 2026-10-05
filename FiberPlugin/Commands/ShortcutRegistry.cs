using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using FiberPlugin.Core;
using AcUtils = Autodesk.AutoCAD.Internal.Utils;

namespace FiberPlugin.Commands
{
    /// <summary>
    /// Registra no AutoCAD os atalhos dos comandos (ShortcutSettings) como comandos de verdade: digitar FLC roda o
    /// FIBRA_LANCAR_CABO, com as mesmas opções dele (ex.: usar a seleção feita antes). Também diz se um atalho bate com
    /// algo que já existe: um comando do AutoCAD ou de outro aplicativo, um comando LISP ou um atalho do acad.pgp.
    /// Usa Autodesk.AutoCAD.Internal.Utils.AddCommand/RemoveCommand, o jeito de criar comandos depois de carregar a DLL.
    /// </summary>
    internal static class ShortcutRegistry
    {
        private const string Group = "FIBRA_ATALHOS";
        private const string PgpFile = "acad.pgp";

        private static readonly Dictionary<string, string> Registered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string>? _pgp;

        /// <summary>Atalho ativo do comando ("" se não houver).</summary>
        public static string AliasFor(string command) =>
            Registered.FirstOrDefault(r => r.Value.Equals(command, StringComparison.OrdinalIgnoreCase)).Key ?? "";

        /// <summary>
        /// O que já existe com esse nome no AutoCAD (null se estiver livre). Os atalhos que o próprio plugin registrou não
        /// contam como conflito.
        /// </summary>
        public static string? Conflict(string alias)
        {
            alias = ShortcutSettings.Normalize(alias);
            if (alias.Length == 0 || Registered.ContainsKey(alias)) return null;
            if (PgpAliases().TryGetValue(alias, out string? target)) return $"já é atalho do AutoCAD para {target} (acad.pgp)";
            try
            {
                if (AcUtils.IsCommandDefined(alias)) return "já é um comando do AutoCAD ou de outro aplicativo";
                if (AcUtils.IsLispCommandDefined(alias)) return "já é um comando LISP";
            }
            catch (System.Exception) { /* Sem como consultar: fica só a conferência do acad.pgp */ }
            return null;
        }

        /// <summary>
        /// Troca os atalhos ativos pelos de <paramref name="map"/> (comando → atalho). Atalho que bate com algo que já
        /// existe não é registrado. Retorna os avisos dos que ficaram de fora.
        /// </summary>
        public static List<string> Apply(IDictionary<string, string> map)
        {
            foreach (string alias in Registered.Keys.ToList())
            {
                try { AcUtils.RemoveCommand(Group, alias); } catch (System.Exception) { }
            }
            Registered.Clear();
            _pgp = null; // Relê o acad.pgp (pode ter mudado)

            var warnings = new List<string>();
            Dictionary<string, (MethodInfo Method, CommandFlags Flags)> targets = CommandMethods();
            foreach (var pair in map)
            {
                string alias = ShortcutSettings.Normalize(pair.Value);
                if (alias.Length == 0 || ShortcutSettings.SyntaxError(alias) != null) continue;
                if (!targets.TryGetValue(pair.Key, out var target)) continue;
                if (Registered.ContainsKey(alias))
                {
                    warnings.Add($"{alias} ({pair.Key}): repetido com o atalho de {Registered[alias]}.");
                    continue;
                }
                if (Conflict(alias) is string conflict)
                {
                    warnings.Add($"{alias} ({pair.Key}): {conflict}.");
                    continue;
                }

                MethodInfo method = target.Method;
                AcUtils.AddCommand(Group, alias, alias, target.Flags, () => Invoke(method));
                Registered[alias] = pair.Key;
            }
            return warnings;
        }

        /// <summary>Roda o comando como se ele tivesse sido digitado; um erro aparece igual ao do próprio comando.</summary>
        private static void Invoke(MethodInfo method)
        {
            object? instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
            try
            {
                method.Invoke(instance, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        /// <summary>Comandos do plugin: nome → método e opções do [CommandMethod].</summary>
        private static Dictionary<string, (MethodInfo, CommandFlags)> CommandMethods()
        {
            var methods = new Dictionary<string, (MethodInfo, CommandFlags)>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in typeof(ShortcutRegistry).Assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method.GetCustomAttribute<CommandMethodAttribute>() is CommandMethodAttribute attribute && method.GetParameters().Length == 0)
                        methods[attribute.GlobalName] = (method, attribute.Flags);
                }
            }
            return methods;
        }

        /// <summary>Atalhos do acad.pgp em uso (atalho → comando), lidos uma vez.</summary>
        private static Dictionary<string, string> PgpAliases()
        {
            if (_pgp != null) return _pgp;
            _pgp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = HostApplicationServices.Current.FindFile(PgpFile, HostApplicationServices.WorkingDatabase, FindFileHint.Default);
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    int comma = line.IndexOf(',');
                    if (line.Length == 0 || line.StartsWith(";") || comma <= 0) continue;
                    string alias = line.Substring(0, comma).Trim().ToUpperInvariant();
                    string target = line.Substring(comma + 1).Split(',')[0].Trim().TrimStart('*').ToUpperInvariant();
                    if (alias.Length > 0 && !_pgp.ContainsKey(alias)) _pgp[alias] = target;
                }
            }
            catch (System.Exception) { /* Sem acad.pgp: nada a conferir */ }
            return _pgp;
        }
    }
}
