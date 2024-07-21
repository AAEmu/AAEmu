using System.Diagnostics;
using AAEmu.Commons.Utils.AAPak;
using AAEmu.Game.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;

namespace AAEmu.PerformanceTests.Commons.Utils;

public class AAPakTest
{
    private readonly ClientDataConfig? _configuration;

    public AAPakTest()
    {
        _configuration = new ConfigurationBuilder()
            .AddUserSecrets<AAPakTest>()
            .Build()
            .GetSection("ClientData:ClientData")
            .Get<ClientDataConfig>();
    }

    [Benchmark]
    public async Task ExportFileAsStreamClonedAsync()
    {
        if (_configuration is null) { return; }

        var validSource = GetValidGamePackPath();

        var sut = new AAPak(validSource);

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        using var stream = await sut.ExportFileAsStreamClonedAsync(validSource);
        stopwatch.Stop();
    }

    [Benchmark]
    public void ExportFileAsStreamCloned()
    {
        if (_configuration is null) { return; }

        var validSource = GetValidGamePackPath();

        var sut = new AAPak(validSource);

        Stopwatch stopwatch2 = new Stopwatch();
        stopwatch2.Start();
        using var stream2 = sut.ExportFileAsStreamCloned(validSource);
        stopwatch2.Stop();
    }

    private string GetValidGamePackPath()
    {
        if (_configuration is null) { return string.Empty; }

        foreach (var source in _configuration.Sources)
        {
            if (source.EndsWith("game_pak") && File.Exists(source))
            {
                return source;
            }

            if (Directory.Exists(source))
            {
                var potentialPath = Path.Combine(source, "game_pak");
                if (File.Exists(potentialPath))
                {
                    return potentialPath;
                }
            }
        }

        return string.Empty;
    }
}
