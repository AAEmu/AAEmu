namespace AAEmu.PerformanceTests.Commons.Utils;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;

public class WorldManagerLoadTest
{
    public WorldManagerLoadTest()
    {
        AppConfiguration.Instance.ClientData = new ConfigurationBuilder()
           .AddUserSecrets<ClientFileManagerTest>()
           .Build()
           .GetSection("ClientData:ClientData")
           .Get<ClientDataConfig>();

        ClientFileManager.Initialize();
    }

    [Benchmark]
    public async Task LoadAsync()
    {
        var worldManager = new WorldManager();
        await worldManager.LoadAsync();
    }

    [Benchmark]
    public void Load()
    {
        var worldManager = new WorldManager();
        worldManager.Load();
    }
}
