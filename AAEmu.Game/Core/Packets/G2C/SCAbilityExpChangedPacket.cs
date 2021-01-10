using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAbilityExpChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly byte _ability;
        private readonly int _exp;
        
        public SCAbilityExpChangedPacket(uint objId, AbilityType ability, int exp) : base(SCOffsets.SCAbilityExpChangedPacket, 5)
        {
            _objId = objId;
            _ability = (byte) ability;
            _exp = exp;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_ability);
            stream.Write(_exp);
            return stream;
        }
    }
}
