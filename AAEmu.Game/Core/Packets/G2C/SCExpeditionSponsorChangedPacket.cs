using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionSponsorChangedPacket : GamePacket
    {
        private readonly SystemFaction _faction;
        private readonly bool _success;
        
        public SCExpeditionSponsorChangedPacket(SystemFaction faction, bool success) : base(SCOffsets.SCExpeditionSponsorChangedPacket, 1)
        {
            _faction = faction;
            _success = success;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_faction);
            stream.Write(_success);
            return stream;
        }
    }
}
