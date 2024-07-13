using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionApplicantAcceptPacket : GamePacket
    {
        public CSExpeditionApplicantAcceptPacket() : base(CSOffsets.CSExpeditionApplicantAcceptPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pretenderIds = new List<uint>();
            var count = stream.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var characterId = stream.ReadUInt32();
                pretenderIds.Add(characterId);
            }
            Logger.Debug("CSExpeditionApplicantAcceptPacket");

            ExpeditionManager.Instance.ExpeditionApplicantAccept(Connection.ActiveChar, pretenderIds);
        }
    }
}
