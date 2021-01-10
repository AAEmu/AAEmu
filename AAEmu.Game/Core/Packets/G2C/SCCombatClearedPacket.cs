using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCombatClearedPacket : GamePacket
    {
        private readonly uint _objId;

        public SCCombatClearedPacket(uint objId) : base(SCOffsets.SCCombatClearedPacket, 5)
        {
            _objId = objId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            return stream;
        }
    }
}
