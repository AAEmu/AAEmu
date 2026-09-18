using AAEmu.Game.Utils.Scripts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AAEmu.UnitTests.Game.Utils.Scripts;

public class ScriptCompilerTests
{
    [Test]
    public async Task ReloadCompilesParallelEvenWhenAssemblyWasNotInDiscoverySnapshot()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var discovered = paths.Where(p => Path.GetFileName(p) != "System.Threading.Tasks.Parallel.dll")
            .Select(p => MetadataReference.CreateFromFile(p));
        var source = CSharpSyntaxTree.ParseText("""
            public class ReloadProbe
            {
                public static int Run()
                {
                    var count = 0;
                    System.Threading.Tasks.Parallel.For(0, 4, i => System.Threading.Interlocked.Increment(ref count));
                    return count;
                }
            }
            """);
        var compilation = CSharpCompilation.Create("ReloadProbe", [source],
            ScriptCompiler.IncludeRuntimeDependencies(discovered),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        await Assert.That(result.Success).IsTrue();
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        await Assert.That((int)assembly.GetType("ReloadProbe")!.GetMethod("Run")!.Invoke(null, null)!).IsEqualTo(4);
    }
}
