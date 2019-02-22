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
            stream.Write(_faction.Id);
            stream.Write(_faction.AggroLink);
            stream.Write(_faction.MotherId);
            stream.Write(_faction.Name);
            stream.Write(_faction.OwnerId);
            stream.Write(_faction.OwnerName);
            stream.Write(_faction.UnitOwnerType);
            stream.Write(_faction.PoliticalSystem);
            stream.Write(_faction.Created);
            stream.Write(_faction.DiplomacyTarget);
            stream.Write((byte)0); // allowChangeName

            stream.Write(_success);
            return stream;
        }
    }
}
