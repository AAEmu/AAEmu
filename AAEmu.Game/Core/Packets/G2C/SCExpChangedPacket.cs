using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly int _exp;
        private readonly bool _shouldAddAbilityExp;

        public SCExpChangedPacket(uint objId, int exp, bool shouldAddAbilityExp) : base(SCOffsets.SCExpChangedPacket, 5)
        {
            _objId = objId;
            _exp = exp;
            _shouldAddAbilityExp = shouldAddAbilityExp;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_exp);
            stream.Write(_shouldAddAbilityExp);
            return stream;
        }
    }
}
