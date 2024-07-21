namespace AAEmu.UnitTests.Commons.Utils;

using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AAEmu.Commons.Utils.AAPak;
using AAEmu.Game.IO;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

public class AAPakTest
{
    private readonly ClientDataConfig _configuration;
    private readonly ITestOutputHelper _outputHelper;

    public AAPakTest(ITestOutputHelper outputHelper)
    {
        _configuration = new ConfigurationBuilder()
            .AddUserSecrets<AAPakTest>()
            .Build()
            .GetSection("ClientData:ClientData")
            .Get<ClientDataConfig>();
        _outputHelper = outputHelper;
    }

    [SkippableFact]
    public async Task ExportFileAsStreamClonedTestAsync()
    {
        if (_configuration is null)
        {
            Skip.If(true, "ClientData configuration is missing");
        }

        var validSource = GetValidGamePackPath();

        var sut = new AAPak(validSource);

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        using var stream = await sut.ExportFileAsStreamClonedAsync(validSource);
        stopwatch.Stop();
        _outputHelper.WriteLine(stopwatch.ElapsedMilliseconds.ToString());

        Stopwatch stopwatch2 = new Stopwatch();
        stopwatch2.Start();
        using var stream2 = sut.ExportFileAsStreamCloned(validSource);
        stopwatch2.Stop();
        _outputHelper.WriteLine(stopwatch2.ElapsedMilliseconds.ToString());
    }

    private string GetValidGamePackPath()
    {
        if (_configuration is null)
        {
            Skip.IfNot(true, "ClientData configuration is missing");
        }

        if (_configuration.Sources is null)
        {
            return string.Empty;
        }

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
