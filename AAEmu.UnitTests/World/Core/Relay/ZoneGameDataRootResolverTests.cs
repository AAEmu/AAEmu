using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class ZoneGameDataRootResolverTests
{
    [Test]
    public async Task ConfiguredOnly_ReturnsExistingDirectory()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var root = ZoneGameDataRootResolver.Resolve(null, dir.FullName, Directory.Exists);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(dir.FullName));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Test]
    public async Task OverrideOnly_ReturnsExistingDirectory()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var root = ZoneGameDataRootResolver.Resolve(dir.FullName, null, Directory.Exists);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(dir.FullName));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Test]
    public async Task Override_TakesPrecedenceOverConfig()
    {
        var envDir = Directory.CreateTempSubdirectory();
        var cfgDir = Directory.CreateTempSubdirectory();
        try
        {
            var root = ZoneGameDataRootResolver.Resolve(envDir.FullName, cfgDir.FullName, Directory.Exists);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(envDir.FullName));
        }
        finally
        {
            envDir.Delete(true);
            cfgDir.Delete(true);
        }
    }

    [Test]
    public async Task InvalidOverride_FallsThroughToConfig()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var cfgDir = Directory.CreateTempSubdirectory();
        try
        {
            var root = ZoneGameDataRootResolver.Resolve(missing, cfgDir.FullName, Directory.Exists);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(cfgDir.FullName));
        }
        finally
        {
            cfgDir.Delete(true);
        }
    }

    [Test]
    public async Task InvalidConfig_DoesNotBlockValidOverride()
    {
        var envDir = Directory.CreateTempSubdirectory();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var root = ZoneGameDataRootResolver.Resolve(envDir.FullName, missing, Directory.Exists);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(envDir.FullName));
        }
        finally
        {
            envDir.Delete(true);
        }
    }

    [Test]
    public async Task BothInvalid_ReturnsNull()
    {
        var missingA = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var missingB = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var root = ZoneGameDataRootResolver.Resolve(missingA, missingB, Directory.Exists);
        await Assert.That(root).IsNull();
    }

    [Test]
    public async Task InvalidPath_FallsThroughToConfig()
    {
        var cfgDir = Directory.CreateTempSubdirectory();
        try
        {
            var root = ZoneGameDataRootResolver.Resolve("\0invalid", cfgDir.FullName, Directory.Exists);
            await Assert.That(root).IsEqualTo(Path.GetFullPath(cfgDir.FullName));
        }
        finally
        {
            cfgDir.Delete(true);
        }
    }

    [Test]
    public async Task BothUnset_ReturnsNull()
    {
        await Assert.That(ZoneGameDataRootResolver.Resolve(null, "  ", Directory.Exists)).IsNull();
    }
}
