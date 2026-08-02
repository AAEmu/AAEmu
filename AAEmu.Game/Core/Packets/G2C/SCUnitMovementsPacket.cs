using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitMovementsPacket((uint id, MoveType type)[] movements)
    : GamePacket(SCOffsets.SCUnitMovementsPacket, 1) // TODO ... SCOneUnitMovementPacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)movements.Length); // TODO ... max size is 400
        foreach (var (id, type) in movements)
        {
            stream.WriteBc(id);
            stream.Write((byte)type.Type); // CN 10.0.2.13: type discriminant before body (same as SCOneUnitMovement)
            stream.Write(type);
        }

        return stream;
    }
}
