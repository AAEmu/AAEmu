using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAskRiskyTeamActionPacket : GamePacket
    {
        public CSAskRiskyTeamActionPacket() : base(0x087, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var id = stream.ReadUInt32();
            var riskyAction = stream.ReadByte(); // ra

            _log.Warn("AskRiskyTeamAction, TeamId: {0}, Id: {1}, RiskyAction: {2}", teamId, id, riskyAction);
        }
    }
}
