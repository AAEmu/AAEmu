using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitMovementsPacket : GamePacket // TODO ... SCOneUnitMovementPacket
    {
        private (uint id, MoveType type)[] _movements;

        public SCUnitMovementsPacket((uint id, MoveType type)[] movements) : base(SCOffsets.SCUnitMovementsPacket, 5)
        {
            _movements = movements;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((ushort) _movements.Length); // TODO ... max size is 400
            foreach (var (id, type) in _movements)
            {
                stream.WriteBc(id);
                stream.Write(type);
            }

            return stream;
        }
    }
}
