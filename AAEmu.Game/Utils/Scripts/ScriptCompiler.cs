using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLog;

namespace AAEmu.Game.Utils.Scripts
{
    public static class ScriptCompiler
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static Assembly _assembly;
        private static Dictionary<string, ScriptObject> _scriptsObjects = new Dictionary<string, ScriptObject>();

        public static bool Compile()
        {
            EnsureDirectory("Scripts/");

            if (!CompileScripts(out var assembly))
                return false;

            _assembly = assembly;

            if (_assembly != null)
                OnLoad();

            return true;
        }

        public static void Clear()
        {
            _scriptsObjects.Clear();
        }

        private static void OnLoad()
        {
            var hasErrors = false;
            _scriptsObjects.Clear();
            var types = _assembly.GetTypes();
            foreach (var type in types)
            {
                if (type.IsNested)
                    continue;
                if (type.IsAbstract)
                    continue;
                try
                {
                    var obj = Activator.CreateInstance(type);
                    var script = new ScriptObject(type, obj);
                    _scriptsObjects.Add(script.Name, script);
                    script.Invoke("OnLoad");
                }
                catch (Exception e)
                {
                    hasErrors = true;
                    _log.Error($"Error in {type}");
                    _log.Error(e);
                }
            }
            if (hasErrors)
                _log.Warn($"There were some errors when compiling the user scripts !");
                // throw new Exception("There were errors in the user scripts !");
        }

        public static bool CompileScripts(out Assembly assembly)
        {
            _log.Info("Compiling scripts...");
            var files = GetScripts("*.cs");
            var isOk = true;

            if (files.Length == 0)
            {
                _log.Info("Compile done (no files found)");
                assembly = null;
                return true;
            }

            var syntaxTrees = ParseScripts(files);
            var assemblyName = Path.GetRandomFileName();

            var references = new List<MetadataReference>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(p => !p.IsDynamic && !string.IsNullOrEmpty(p.Location)))
                references.Add(MetadataReference.CreateFromFile(asm.Location));

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assembly assemblyResult = null;

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (result.Success)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    assemblyResult = AssemblyLoadContext.Default.LoadFromStream(ms);
                }

                isOk = Display(result.Diagnostics);
            }

            assembly = assemblyResult;
            return (assemblyResult != null) && (isOk);
        }

        private static bool Display(ImmutableArray<Diagnostic> diagnostics)
        {
            bool res = true;
            if (diagnostics.Length == 0)
            {
                _log.Info("Compile done (0 errors, 0 warnings)");
            }
            else
            {
                var errorCount = diagnostics.Count(x => x.Severity == DiagnosticSeverity.Error);
                var warningCount = diagnostics.Count(x => x.Severity == DiagnosticSeverity.Warning);

                if (errorCount > 0)
                {
                    res = false;
                    _log.Error("Compile failed ({0} errors, {1} warnings)", errorCount, warningCount);
                }
                else
                    _log.Info("Compile done ({0} errors, {1} warnings)", errorCount, warningCount);

                var result = diagnostics.Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error ||
                    diagnostic.Severity == DiagnosticSeverity.Warning);
                foreach (var diagnostic in result)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                        _log.Error(diagnostic);
                    else
                        _log.Warn(diagnostic);
                }
            }

            return res;
        }

        private static void EnsureDirectory(string dir)
        {
            var path = Path.Combine(Helpers.BaseDirectory, dir);

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private static string[] GetScripts(string filter)
        {
            var list = new List<string>();
            GetScripts(list, Path.Combine(Helpers.BaseDirectory, "Scripts"), filter);
            return list.ToArray();
        }

        private static void GetScripts(List<string> list, string path, string filter)
        {
            foreach (var dir in Directory.GetDirectories(path))
                GetScripts(list, dir, filter);

            list.AddRange(Directory.GetFiles(path, filter));
        }

        private static IEnumerable<SyntaxTree> ParseScripts(IEnumerable<string> list)
        {
            var syntaxTrees = new List<SyntaxTree>();
            foreach (var path in list)
            {
                var script = FileManager.GetFileContents(path);
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(script));
            }

            return syntaxTrees;
        }
    }
}
