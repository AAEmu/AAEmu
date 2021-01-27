using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGamePointChangedPacket : GamePacket
    {
        private readonly byte _kind;
        private readonly int _amount;

        // TODO kind:
        // 0 - honor
        // 1 - vocation(living)

        public SCGamePointChangedPacket(byte kind, int amount) : base(SCOffsets.SCGamePointChangedPacket, 5)
        {
            _kind = kind;
            _amount = amount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_kind);
            stream.Write(_amount);
            return stream;
        }
    }
}
