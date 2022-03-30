using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLevelChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly byte _level;

        public SCLevelChangedPacket(uint objId, byte level) : base(SCOffsets.SCLevelChangedPacket, 5)
        {
            _objId = objId;
            _level = level;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_level);

            return stream;
        }
    }
}
