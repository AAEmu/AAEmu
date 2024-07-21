using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace AAEmu.PerformanceTests;

internal sealed class Program
{
    private static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new DebugInProcessConfig());
    }
}
