using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetPingPosPacket : GamePacket
    {
        public CSSetPingPosPacket() : base(CSOffsets.CSSetPingPosPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var owner = Connection.ActiveChar;
            var teamId = stream.ReadUInt32(); // teamId
            owner.LocalPingPosition.Read(stream);
            if (teamId > 0)
            {
                TeamManager.Instance.SetPingPos(owner, teamId, owner.LocalPingPosition);
            }
            else
            {
                owner.SendPacket(new SCTeamPingPosPacket(owner.LocalPingPosition));
            }
        }
    }
}
