using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

/// <summary>
/// Regression coverage for the housing-permissions change to <see cref="Doodad.Data"/>.
///
/// Assigning <see cref="Doodad.Data"/> has always persisted the change. Devote
/// (DoodadFuncDevote.PublishProgress) and ActCount (DoodadFuncActCount.TryApply) both rely on that to
/// keep their counters across a restart. Turning the setter into a plain field assignment silently
/// dropped every one of those saves, so the counters reverted on reload even though the in-memory
/// value looked correct. These tests drive the real writers and round-trip the saved value through a
/// freshly loaded doodad.
/// </summary>
public class DoodadDataPersistenceTests
{
    [Test]
    public async Task DevoteContribution_IsSavedAndSurvivesReload()
    {
        var doodad = new RecordingDoodad();

        // This is the exact write DoodadFuncDevote.PublishProgress performs; PublishProgress itself
        // also broadcasts, which needs a live world and is covered by the feature's own tests.
        doodad.Data = 3;

        await Assert.That(doodad.PersistCount).IsEqualTo(1);
        await Assert.That(doodad.PersistedData).IsEqualTo(3);

        var reloaded = await ReloadFrom(doodad.PersistedData);
        await Assert.That(reloaded.Data).IsEqualTo(3);
    }

    [Test]
    public async Task ActCountUse_IsSavedAndSurvivesReload()
    {
        var doodad = new RecordingDoodad();
        var func = new DoodadFunc { Count = 3 };

        DoodadFuncActCount.TryApply(doodad, func, out _);

        await Assert.That(doodad.PersistCount).IsEqualTo(1);
        await Assert.That(doodad.PersistedData).IsEqualTo(1);

        var reloaded = await ReloadFrom(doodad.PersistedData);
        await Assert.That(reloaded.Data).IsEqualTo(1);
    }

    [Test]
    public async Task ConsecutiveActCountUses_EachSaveAndTheFinalCountSurvives()
    {
        var doodad = new RecordingDoodad();
        var func = new DoodadFunc { Count = 5 };

        DoodadFuncActCount.TryApply(doodad, func, out _); // Data -> 1
        DoodadFuncActCount.TryApply(doodad, func, out _); // Data -> 2

        await Assert.That(doodad.PersistCount).IsEqualTo(2);
        await Assert.That(doodad.PersistedData).IsEqualTo(2);

        var reloaded = await ReloadFrom(doodad.PersistedData);
        await Assert.That(reloaded.Data).IsEqualTo(2);
    }

    [Test]
    public async Task AssigningTheSameValue_DoesNotSaveAgain()
    {
        var doodad = new RecordingDoodad();

        doodad.Data = 4;
        doodad.Data = 4;

        await Assert.That(doodad.PersistCount).IsEqualTo(1);
    }

    private static async Task<Doodad> ReloadFrom(int? persistedData)
    {
        await Assert.That(persistedData).IsNotNull();
        var reloaded = new Doodad();
        reloaded.SetData(persistedData!.Value); // loader path; must not re-persist
        return reloaded;
    }

    /// <summary>
    /// Stands in for the database row: <see cref="Doodad.PersistDataOnChange"/> is the seam the setter
    /// calls, so overriding it records exactly what a save would have written without a live MySQL.
    /// </summary>
    private sealed class RecordingDoodad : Doodad
    {
        public int PersistCount { get; private set; }
        public int? PersistedData { get; private set; }

        protected override void PersistDataOnChange()
        {
            PersistCount++;
            PersistedData = Data;
        }
    }
}
