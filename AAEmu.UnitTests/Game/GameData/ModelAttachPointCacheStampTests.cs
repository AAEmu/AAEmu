using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The attach point cache is reused whenever its identity matches. The identity has to include the
/// resolver's format version, or a cache produced under the previous resolution rules keeps being served
/// even though nothing about its inputs has changed - which is exactly how a resolver fix can appear to
/// do nothing at all.
/// </summary>
public class ModelAttachPointCacheStampTests
{
    private static readonly string[] Sources =
    [
        "C:/game/data/one|1024|2026-08-01T00:00:00.0000000Z",
        "C:/game/data/two|dir"
    ];

    [Test]
    public async Task FormatVersionChange_InvalidatesAnOtherwiseMatchingCache()
    {
        // Same sources, byte for byte. Only the resolver changed.
        var written = ModelAttachPointGameData.ComposeCacheStamp(1, Sources);
        var current = ModelAttachPointGameData.ComposeCacheStamp(2, Sources);

        await Assert.That(written).IsNotEqualTo(current);
        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(written, current)).IsFalse();
    }

    [Test]
    public async Task UnchangedInputs_KeepTheCache()
    {
        var written = ModelAttachPointGameData.ComposeCacheStamp(2, Sources);
        var current = ModelAttachPointGameData.ComposeCacheStamp(2, Sources);

        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(written, current)).IsTrue();
    }

    [Test]
    public async Task ChangedSource_InvalidatesTheCache()
    {
        var written = ModelAttachPointGameData.ComposeCacheStamp(2, Sources);
        var current = ModelAttachPointGameData.ComposeCacheStamp(2,
            ["C:/game/data/one|2048|2026-08-02T00:00:00.0000000Z", "C:/game/data/two|dir"]);

        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(written, current)).IsFalse();
    }

    [Test]
    public async Task AddedOrRemovedSource_InvalidatesTheCache()
    {
        var current = ModelAttachPointGameData.ComposeCacheStamp(2, Sources);

        var fewer = ModelAttachPointGameData.ComposeCacheStamp(2, [Sources[0]]);
        var more = ModelAttachPointGameData.ComposeCacheStamp(2, [.. Sources, "C:/game/data/three|dir"]);

        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(fewer, current)).IsFalse();
        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(more, current)).IsFalse();
    }

    [Test]
    public async Task SourceOrder_IsPartOfTheIdentity()
    {
        var forward = ModelAttachPointGameData.ComposeCacheStamp(2, Sources);
        var reversed = ModelAttachPointGameData.ComposeCacheStamp(2, [Sources[1], Sources[0]]);

        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(reversed, forward)).IsFalse();
    }

    [Test]
    public async Task StampCarriesTheFormatVersion()
    {
        var stamp = ModelAttachPointGameData.ComposeCacheStamp(7, Sources);

        await Assert.That(stamp.StartsWith("v7", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MissingStamp_IsNeverCurrent()
    {
        // A cache written before the stamp existed, or one truncated on disk, must rebuild rather than
        // be trusted.
        var current = ModelAttachPointGameData.ComposeCacheStamp(2, Sources);

        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(null, current)).IsFalse();
        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(string.Empty, current)).IsFalse();
    }

    [Test]
    public async Task NoSources_StillProducesAVersionedIdentity()
    {
        var v1 = ModelAttachPointGameData.ComposeCacheStamp(1, []);
        var v2 = ModelAttachPointGameData.ComposeCacheStamp(2, []);

        await Assert.That(ModelAttachPointGameData.IsCacheCurrent(v1, v2)).IsFalse();
    }
}
