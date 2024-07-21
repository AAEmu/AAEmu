namespace AAEmu.PerformanceTests.Commons.Utils;

using AAEmu.Game.IO;
using AAEmu.Game.Models;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;

public class ClientFileManagerTest
{
    private List<string> _testingFiles = [
        "game/worlds/main_world/level_design/zone/129/client/subzone_area.xml"
    ];

    public ClientFileManagerTest()
    {
        AppConfiguration.Instance.ClientData = new ConfigurationBuilder()
           .AddUserSecrets<ClientFileManagerTest>()
           .Build()
           .GetSection("ClientData:ClientData")
           .Get<ClientDataConfig>();

        ClientFileManager.Initialize();
    }

    [Benchmark]
    public async Task GetFileAsStringAsync()
    {
        await ClientFileManager.GetFileAsStringAsync(_testingFiles[0]);
    }

    [Benchmark]
    public void GetFileAsString()
    {
        ClientFileManager.GetFileAsString(_testingFiles[0]);
    }
}
