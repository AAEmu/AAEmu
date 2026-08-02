using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: i8 worldId, i8 userGrade, i16 myTurn, i16 normalLength,
/// i16 premiumLength, i32 dummyWaitTime. 1.2 sent the premium flag as a bool in place of the grade byte and
/// omitted the wait time entirely.
/// </remarks>
public class SCWorldQueuePacket(byte worldId, bool isPremium, ushort myTurn, ushort normalLenght, ushort premiumLenght, int dummyWaitTime = 0)
    : GamePacket(SCOffsets.SCWorldQueuePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldId);
        stream.Write((byte)(isPremium ? 1 : 0)); // userGrade
        stream.Write((short)myTurn);
        stream.Write((short)normalLenght);
        stream.Write((short)premiumLenght);
        stream.Write(dummyWaitTime);
        return stream;
    }
}
