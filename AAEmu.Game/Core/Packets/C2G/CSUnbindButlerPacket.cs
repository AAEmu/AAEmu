using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// The packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSUnbindButlerPacket() : GamePacket(CSOffsets.CSUnbindButlerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var result = ButlerManager.Instance.Unbind(character);
        character.SendPacket(new SCButlerUnboundPacket((ushort)result.Error));
    }
}
