using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class GameContentRootResolverTests
{
    [Test]
    public async Task ConfiguredRoot_WithConfigConfigurationsAndDb_Wins()
    {
        var cfg = Directory.CreateTempSubdirectory();
        var bas = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(cfg.FullName, "Config.json"), "{}");
            Directory.CreateDirectory(Path.Combine(cfg.FullName, "Configurations"));
            Directory.CreateDirectory(Path.Combine(cfg.FullName, "Data"));
            File.WriteAllText(Path.Combine(cfg.FullName, "Data", "compact.sqlite3"), "x");
            File.WriteAllText(Path.Combine(bas.FullName, "Config.json"), "{}");

            var root = GameContentRootResolver.Resolve(cfg.FullName, bas.FullName);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(cfg.FullName));
        }
        finally
        {
            cfg.Delete(true);
            bas.Delete(true);
        }
    }

    [Test]
    public async Task ConfiguredRoot_ConfigWithoutDb_Throws()
    {
        var cfg = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(cfg.FullName, "Config.json"), "{}");
            Directory.CreateDirectory(Path.Combine(cfg.FullName, "Configurations"));
            await Assert.That(() => GameContentRootResolver.Resolve(cfg.FullName, Path.GetTempPath()))
                .Throws<DirectoryNotFoundException>();
        }
        finally
        {
            cfg.Delete(true);
        }
    }

    [Test]
    public async Task ConfiguredRoot_ConfigAndDbWithoutConfigurations_Throws()
    {
        var cfg = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(cfg.FullName, "Config.json"), "{}");
            Directory.CreateDirectory(Path.Combine(cfg.FullName, "Data"));
            File.WriteAllText(Path.Combine(cfg.FullName, "Data", "compact.sqlite3"), "x");
            await Assert.That(() => GameContentRootResolver.Resolve(cfg.FullName, Path.GetTempPath()))
                .Throws<DirectoryNotFoundException>();
        }
        finally
        {
            cfg.Delete(true);
        }
    }

    [Test]
    public async Task EmptyConfigured_PrefersBaseWithTowerDefsAndDb()
    {
        var bas = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(bas.FullName, "Config.json"), "{}");
            Directory.CreateDirectory(Path.Combine(bas.FullName, "Configurations"));
            File.WriteAllText(Path.Combine(bas.FullName, "Configurations", "TowerDefs.json"), "{}");
            Directory.CreateDirectory(Path.Combine(bas.FullName, "Data"));
            File.WriteAllText(Path.Combine(bas.FullName, "Data", "compact.sqlite3"), "x");

            var root = GameContentRootResolver.Resolve("", bas.FullName);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(bas.FullName));
        }
        finally
        {
            bas.Delete(true);
        }
    }

    [Test]
    public async Task ConfiguredWithoutConfigs_FallsThroughToPreferredBase()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var bas = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(bas.FullName, "Game.Config.json"), "{}");
            Directory.CreateDirectory(Path.Combine(bas.FullName, "Configurations"));
            File.WriteAllText(Path.Combine(bas.FullName, "Configurations", "TowerDefs.json"), "{}");
            Directory.CreateDirectory(Path.Combine(bas.FullName, "Data"));
            File.WriteAllText(Path.Combine(bas.FullName, "Data", "compact.sqlite3"), "x");

            var root = GameContentRootResolver.Resolve(missing, bas.FullName);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(bas.FullName));
        }
        finally
        {
            bas.Delete(true);
        }
    }

    [Test]
    public async Task MissingEverything_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await Assert.That(() => GameContentRootResolver.Resolve(null, missing))
            .Throws<DirectoryNotFoundException>();
    }
}
