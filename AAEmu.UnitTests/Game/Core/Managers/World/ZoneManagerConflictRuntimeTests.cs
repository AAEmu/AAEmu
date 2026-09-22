using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class ZoneManagerConflictRuntimeTests
{
    [Test]
    public async Task StoreReadFailure_StopsStartupWithoutWritingDefaults()
    {
        var store = new FailingReadStore();
        var manager = new ZoneManager(null, store);

        await Assert.That(manager.StartConflictCycles).Throws<InvalidOperationException>();
        await Assert.That(store.SaveCount).IsEqualTo(0);
    }

    private sealed class FailingReadStore : IConflictZoneRuntimeStore
    {
        public int SaveCount { get; private set; }

        public IReadOnlyDictionary<ushort, ConflictZoneRuntimeState> LoadAll() =>
            throw new InvalidOperationException("store unavailable");

        public void Save(ConflictZoneRuntimeState state) => SaveCount++;
    }
}
