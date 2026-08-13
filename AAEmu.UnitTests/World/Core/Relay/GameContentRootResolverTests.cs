using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class GameContentRootResolverTests
{
    [Test]
    public async Task ConfiguredRoot_Bootable_Wins()
    {
        var cfg = Directory.CreateTempSubdirectory();
        var bas = Directory.CreateTempSubdirectory();
        try
        {
            MakeBootable(cfg.FullName);
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
    public async Task ConfiguredRoot_MissingDirectory_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await Assert.That(() => GameContentRootResolver.Resolve(missing, Path.GetTempPath()))
            .Throws<DirectoryNotFoundException>();
    }

    [Test]
    public async Task ConfiguredRoot_Incomplete_ThrowsWithoutFallback()
    {
        var cfg = Directory.CreateTempSubdirectory();
        var bas = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(cfg.FullName, "Config.json"), "{}");
            MakeBootable(bas.FullName);

            await Assert.That(() => GameContentRootResolver.Resolve(cfg.FullName, bas.FullName))
                .Throws<DirectoryNotFoundException>();
        }
        finally
        {
            cfg.Delete(true);
            bas.Delete(true);
        }
    }

    [Test]
    public async Task EmptyConfigured_UsesBootableBaseDirectory()
    {
        var bas = Directory.CreateTempSubdirectory();
        try
        {
            MakeBootable(bas.FullName);
            Directory.CreateDirectory(Path.Combine(bas.FullName, "Configurations"));
            File.WriteAllText(Path.Combine(bas.FullName, "Configurations", "TowerDefs.json"), "{}");

            var root = GameContentRootResolver.Resolve("", bas.FullName);
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

    private static void MakeBootable(string root)
    {
        File.WriteAllText(Path.Combine(root, "Config.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "Configurations"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        File.WriteAllText(Path.Combine(root, "Data", "compact.sqlite3"), "x");
    }
}
