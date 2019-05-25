using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChallengeDuelPacket : GamePacket
    {
        public CSChallengeDuelPacket() : base(0x050, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var challengedId = stream.ReadUInt32(); // Id the one we challenged to a duel

            var challengerId = Connection.ActiveChar.Id;

            Connection.ActiveChar.BroadcastPacket(new SCDuelChallengedPacket(challengerId), false); // only to the enemy

            _log.Warn("ChallengeDuel, challengedId: {0}", challengedId);
        }
    }
}
