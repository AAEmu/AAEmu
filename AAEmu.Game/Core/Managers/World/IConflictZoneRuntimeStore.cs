using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Managers.World;

public interface IConflictZoneRuntimeStore
{
    IReadOnlyDictionary<ushort, ConflictZoneRuntimeState> LoadAll();
    void Save(ConflictZoneRuntimeState state);
}
