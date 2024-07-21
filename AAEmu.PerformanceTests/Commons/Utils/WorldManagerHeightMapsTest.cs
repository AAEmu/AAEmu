namespace AAEmu.PerformanceTests.Commons.Utils;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;

public class WorldManagerHeightMapsTest
{
    public WorldManagerHeightMapsTest()
    {
        AppConfiguration.Instance.ClientData = new ConfigurationBuilder()
           .AddUserSecrets<ClientFileManagerTest>()
           .Build()
           .GetSection("ClientData:ClientData")
           .Get<ClientDataConfig>();

        ClientFileManager.Initialize();
    }

    [Benchmark]
    public async Task LoadHeightmapsAsync()
    {
        var worldManager = new WorldManager();
        await worldManager.LoadHeightmapsAsync();
    }

    [Benchmark]
    public void LoadHeightmaps()
    {
        var worldManager = new WorldManager();
        worldManager.LoadHeightmaps();
    }
}
