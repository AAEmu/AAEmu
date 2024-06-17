using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionSetExpeditionMotdPacket : GamePacket
    {
        public CSFactionSetExpeditionMotdPacket() : base(CSOffsets.CSFactionSetExpeditionMotdPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var motdTitle = stream.ReadString();
            var motdContent = stream.ReadString();

            Logger.Debug($"CSFactionSetExpeditionMotdPacket: id={id}, motdTitle={motdTitle}, motdContent={motdContent}");

            ExpeditionManager.Instance.SetMotd(Connection.ActiveChar, motdTitle, motdContent);
        }
    }
}
