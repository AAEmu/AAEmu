using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NLog;

namespace AAEmu.Game.Utils.Scripts;

public static class ScriptCompiler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static Assembly _assembly;
    private static Dictionary<string, ScriptObject> _scriptsObjects = [];

    public static bool Compile()
    {
        EnsureDirectory("Scripts/");

        if (!CompileScripts(out var assembly, out _))
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
                Logger.Error($"Error in {type}");
                Logger.Error(e);
            }
        }
        if (hasErrors)
            Logger.Warn($"There were some errors when compiling the user scripts !");
        // throw new Exception("There were errors in the user scripts !");
    }

    public static bool CompileScriptsWithAllDependencies(out Assembly assembly, out ImmutableArray<Diagnostic> diagnostics)
    {
        var references = new List<MetadataReference>();

        foreach (AssemblyName assemblyName in Assembly.GetEntryAssembly().GetReferencedAssemblies())
            references.Add(MetadataReference.CreateFromFile(Assembly.Load(assemblyName).Location));

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(p => !p.IsDynamic && !string.IsNullOrEmpty(p.Location)))
            references.Add(MetadataReference.CreateFromFile(asm.Location));

        references = references.Distinct().ToList();

        return CompileScripts(references, out assembly, out diagnostics);
    }

    public static bool CompileScripts(out Assembly assembly, out ImmutableArray<Diagnostic> diagnostics)
    {
        var references = new List<MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(p => !p.IsDynamic && !string.IsNullOrEmpty(p.Location)))
            references.Add(MetadataReference.CreateFromFile(asm.Location));

        return CompileScripts(references, out assembly, out diagnostics);
    }

    public static bool CompileScripts(IEnumerable<MetadataReference> references, out Assembly assembly, out ImmutableArray<Diagnostic> diagnostics)
    {
        Logger.Info("Compiling scripts...");
        var files = GetScripts("*.cs");
        var isOk = true;

        if (files.Length == 0)
        {
            Logger.Info("Compile done (no files found)");
            assembly = null;
            diagnostics = ImmutableArray<Diagnostic>.Empty;
            return true;
        }

        var syntaxTrees = ParseScripts(files);

        //DebugCompilation(syntaxTrees, references);

        Assembly assemblyResult = null;
        var assemblyName = Path.GetRandomFileName();

        var compileOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            compileOptions);

        using (var ms = new MemoryStream())
        {
            var result = compilation.Emit(ms);
            diagnostics = result.Diagnostics;
            if (result.Success)
            {
                ms.Seek(0, SeekOrigin.Begin);
                assemblyResult = AssemblyLoadContext.Default.LoadFromStream(ms);
            }

            isOk = Display(result.Diagnostics, syntaxTrees);
        }

        assembly = assemblyResult;

        return (assemblyResult != null) && (isOk);
    }

    // Only for debugging purposes
#pragma warning disable IDE0051 // Remove unused private members
    private static void DebugCompilation(List<SyntaxTree> syntaxTrees, IEnumerable<MetadataReference> references)
    {
        foreach (var syntaxTree in syntaxTrees)
        {
            var assemblyName = Path.GetRandomFileName();

            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                var diagnostics = result.Diagnostics;
                if (result.Success)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                }

                Display(result.Diagnostics, [syntaxTree]);
            }
        }
    }
#pragma warning restore IDE0051 // Remove unused private members

    private static bool Display(ImmutableArray<Diagnostic> diagnostics, List<SyntaxTree> syntaxTrees)
    {
        bool res = true;
        if (diagnostics.Length == 0)
        {
            Logger.Info("Compile done (0 errors, 0 warnings)");
        }
        else
        {
            var errorCount = diagnostics.Count(x => x.Severity == DiagnosticSeverity.Error);
            var warningCount = diagnostics.Count(x => x.Severity == DiagnosticSeverity.Warning);

            if (errorCount > 0)
            {
                res = false;
                Logger.Error("Compile failed ({0} errors, {1} warnings)", errorCount, warningCount);
            }
            else
                Logger.Info("Compile done ({0} errors, {1} warnings)", errorCount, warningCount);

            var result = diagnostics.Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error ||
                diagnostic.Severity == DiagnosticSeverity.Warning);
            foreach (var diagnostic in result)
            {
                //SyntaxTree responsibleSyntaxTree = GetResponsibleSyntaxTree(diagnostic.Location.SourceSpan, syntaxTrees);
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Logger.Error(diagnostic);
                    //Logger.Error("Syntax Tree for Error:\n" + responsibleSyntaxTree.GetRoot().ToFullString());
                }
                else
                {
                    Logger.Warn(diagnostic);
                    //Logger.Warn("Syntax Tree for Warning:\n" + responsibleSyntaxTree.GetRoot().ToFullString());
                }
            }
        }

        return res;
    }

#pragma warning disable IDE0051 // Remove unused private members
    private static SyntaxTree GetResponsibleSyntaxTree(TextSpan location, List<SyntaxTree> syntaxTrees)
    {
        foreach (var syntaxTree in syntaxTrees)
        {
            if (syntaxTree.GetRoot().FullSpan.Contains(location))
            {
                return syntaxTree;
            }
        }
        return null; // Location does not belong to any syntax tree
    }
#pragma warning restore IDE0051 // Remove unused private members

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

    private static List<SyntaxTree> ParseScripts(IEnumerable<string> list)
    {
        var syntaxTrees = new List<SyntaxTree>();
        StringBuilder sb = new();
        foreach (var path in list)
        {
            var script = RenameClasses(path);
            sb.AppendLine(script);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(script));
        }

        var finalString = sb.ToString();
        return syntaxTrees;
    }

    private static string RenameClasses(string filePath)
    {
        var script = FileManager.GetFileContents(filePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(script);
        var root = syntaxTree.GetRoot();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        var dictionaryOldNew = new Dictionary<string, string>();
        foreach (var @class in classes)
        {
            dictionaryOldNew.Add(@class.Identifier.ValueText, "Generated_" + @class.Identifier.ValueText);
        }

        // Rename enums
        var enums = root.DescendantNodes().OfType<EnumDeclarationSyntax>();
        foreach (var @enum in enums)
        {
            dictionaryOldNew.Add(@enum.Identifier.ValueText, "Generated_" + @enum.Identifier.ValueText);
            var newName = "Generated_" + @enum.Identifier.ValueText;
            var newEnum = @enum.WithIdentifier(SyntaxFactory.Identifier(newName));
            root = root.ReplaceNode(@enum, newEnum);
        }

        var finalString = root.ToFullString();

        // Classes with multiple constructors aren't replaced with SyntaxNode
        foreach (var (oldName, newName) in dictionaryOldNew)
        {
            finalString = finalString.Replace($"new {oldName}(", $"new {newName}(");
            finalString = finalString.Replace($"new {oldName}\r\n", $"new {newName}\r\n");
            finalString = finalString.Replace($"new {oldName}\n", $"new {newName}\n");
            finalString = finalString.Replace($"public {oldName}", $"public {newName}");
            finalString = finalString.Replace($" {oldName}(", $" {newName}(");
            finalString = finalString.Replace($"class {oldName}", $"class {newName}");
            finalString = finalString.Replace($"<{oldName}>", $"<{newName}>");
            finalString = finalString.Replace($"typeof({oldName})", $"typeof({newName})");
            finalString = finalString.Replace($"({oldName} ", $"({newName} ");
            finalString = finalString.Replace($" {oldName}.", $" {newName}.");
            finalString = finalString.Replace($" {oldName} ", $" {newName} ");
        }

        return finalString;
    }
}
