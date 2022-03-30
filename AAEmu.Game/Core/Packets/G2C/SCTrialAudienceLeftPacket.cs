using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrialAudienceLeftPacket : GamePacket
    {
        private readonly byte _bc;
        private readonly string _audienceName;

        public SCTrialAudienceLeftPacket(byte bc, string audiencename) : base(SCOffsets.SCTrialAudienceLeftPacket, 5)
        {
            _bc = bc;
            _audienceName = audiencename;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_bc);
            stream.Write(_audienceName);
            return base.Write(stream);
        }
    }
}
