using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitNameChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly string _name;

        public SCUnitNameChangedPacket(uint objId, string name) : base(SCOffsets.SCUnitNameChangedPacket, 5)
        {
            _objId = objId;
            _name = name;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_name);
            return stream;
        }
    }
}
