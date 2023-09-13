using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSaveTutorialPacket : GamePacket
    {
        public CSSaveTutorialPacket() : base(CSOffsets.CSSaveTutorialPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Debug("SaveTutorial, Id: {0}", id);

            var completeId = (ushort)(id / 64);
            var quest = Connection.ActiveChar.Quests.GetCompletedQuest(completeId);
            if (quest == null)
            {
                quest = new CompletedQuest(completeId);
                Connection.ActiveChar.Quests.AddCompletedQuest(quest);
            }

            quest.Body.Set((int)id - completeId * 64, true);
            var body = new byte[8];
            quest.Body.CopyTo(body, 0);
            Connection.SendPacket(new SCTutorialSavedPacket(id, body));
        }
    }
}
