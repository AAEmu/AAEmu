using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSuspectGoingBotTrialPacket : GamePacket
    {
        private readonly uint _type;
        private readonly uint _type2;
        private readonly bool _kicked;

        public SCSuspectGoingBotTrialPacket(uint type, uint type2, bool kicked) : base(SCOffsets.SCSuspectGoingBotTrialPacket, 5)
        {
            _type = type;
            _type2 = type2;
            _kicked = kicked;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_type);
            stream.Write(_type2);
            stream.Write(_kicked);
            return stream;
        }
    }
}
