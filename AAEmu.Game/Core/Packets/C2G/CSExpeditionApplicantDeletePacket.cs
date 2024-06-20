using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionApplicantDeletePacket : GamePacket
    {
        public CSExpeditionApplicantDeletePacket() : base(CSOffsets.CSExpeditionApplicantDeletePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var expeditionId = stream.ReadUInt32(); // type(id)

            Logger.Debug($"CSExpeditionApplicantDeletePacket: character={Connection.ActiveChar.Name}:{Connection.ActiveChar.Id}, expeditionId={expeditionId}");

            ExpeditionManager.Instance.RemovePretender(Connection.ActiveChar, expeditionId);
        }
    }
}
