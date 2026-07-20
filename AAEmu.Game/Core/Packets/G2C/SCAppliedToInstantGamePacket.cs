using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppliedToInstantGamePacket(uint battlefieldId, InstantCorps corps, ushort errorMessageId = 0)
    : GamePacket(SCOffsets.SCAppliedToInstantGamePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(battlefieldId);
        stream.Write((byte)corps);
        stream.Write(errorMessageId);
        return stream;
    }
}
