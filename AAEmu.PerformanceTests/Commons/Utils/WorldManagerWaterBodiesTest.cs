namespace AAEmu.PerformanceTests.Commons.Utils;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;

public class WorldManagerWaterBodiesTest
{
    public WorldManagerWaterBodiesTest()
    {
        AppConfiguration.Instance.ClientData = new ConfigurationBuilder()
           .AddUserSecrets<ClientFileManagerTest>()
           .Build()
           .GetSection("ClientData:ClientData")
           .Get<ClientDataConfig>();

        ClientFileManager.Initialize();
    }

    [Benchmark]
    public async Task LoadWaterBodiesAsync()
    {
        var worldManager = new WorldManager();
        await worldManager.LoadAsync();
        await worldManager.LoadWaterBodiesAsync();
    }

    [Benchmark]
    public void LoadWaterBodies()
    {
        var worldManager = new WorldManager();
        worldManager.Load();
        worldManager.LoadWaterBodies();
    }
}
