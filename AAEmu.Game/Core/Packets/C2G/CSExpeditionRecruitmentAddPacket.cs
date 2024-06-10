using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionRecruitmentAddPacket : GamePacket
    {
        public CSExpeditionRecruitmentAddPacket() : base(CSOffsets.CSExpeditionRecruitmentAddPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var interest = stream.ReadUInt16(); // битовый массив из 6 элементов (все выбраны = 63) Dungeon-1, War-2,Naval Battles-4,Raids-8,Adventure-16,Crafting-32
            var day = stream.ReadInt32(); // количество дней заявки 3 или 9
            var introduce = stream.ReadString();

            Logger.Debug("CSExpeditionRecruitmentAddPacket");
            ExpeditionManager.Instance.RecruitmentAdd(Connection, interest, day, introduce);
        }
    }
}
