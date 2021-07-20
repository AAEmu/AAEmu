using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetPingPosPacket : GamePacket
    {
        public CSSetPingPosPacket() : base(CSOffsets.CSSetPingPosPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var hasPing = stream.ReadBoolean();
            var position = new WorldSpawnPosition();
            position.X = stream.ReadSingle();
            position.Y = stream.ReadSingle();
            position.Z = stream.ReadSingle();
            var insId = stream.ReadUInt32();
            
            // _log.Warn("SetPingPos, teamId {0}, hasPing {1}, insId {2}", teamId, hasPing, insId);
            var owner = Connection.ActiveChar;
            owner.LocalPingPosition = position;
            if (teamId > 0)
            {
                TeamManager.Instance.SetPingPos(owner, teamId, hasPing, position, insId);
            }
            else
            {
                owner.SendPacket(new SCTeamPingPosPacket(hasPing, position, insId));
            }
        }
    }
}
