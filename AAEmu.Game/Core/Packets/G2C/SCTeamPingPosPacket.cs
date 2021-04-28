using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamPingPosPacket : GamePacket
    {
        private readonly PingPosition _pingPosition;

        public SCTeamPingPosPacket(PingPosition pingPosition) : base(SCOffsets.SCTeamPingPosPacket, 5)
        {
            _pingPosition = pingPosition;
        }

        public override PacketStream Write(PacketStream stream)
        {
            _pingPosition.Write(stream);

            return stream;
        }
    }
}
