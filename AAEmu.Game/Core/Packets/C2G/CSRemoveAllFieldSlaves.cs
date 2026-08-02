using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so a
/// shared serializer here is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSRemoveAllFieldSlaves() : GamePacket(CSOffsets.CSRemoveAllFieldSlaves, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        // SlaveManager resolves only this character's active summoned hull, withdraws it from
        // the owning Zone, unbinds passengers, and retires all attached field objects with it.
        character.ParentWorld.SlaveManager.RemoveAndDespawnAllActiveOwnedSlaves(character);
    }
}
