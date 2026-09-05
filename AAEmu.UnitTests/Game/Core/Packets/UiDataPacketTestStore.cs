using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Core.Packets;

internal sealed class UiDataPacketTestStore : ICharacterOptionStore
{
    public List<(uint CharacterId, ushort Key, string Value)> Saves { get; } = [];

    public void Save(uint characterId, ushort key, string value) =>
        Saves.Add((characterId, key, value));
}
