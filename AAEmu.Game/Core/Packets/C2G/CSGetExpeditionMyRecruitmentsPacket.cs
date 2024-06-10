using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSGetExpeditionMyRecruitmentsPacket : GamePacket
{
    public CSGetExpeditionMyRecruitmentsPacket() : base(CSOffsets.CSGetExpeditionMyRecruitmentsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var my = stream.ReadBoolean(); // только свои объявления
        var page = stream.ReadInt16(); // текущая страница
        var interest = stream.ReadUInt16(); // битовый массив из 6 элементов (все выбраны = 63) Dungeon-1, War-2,Naval Battles-4,Raids-8,Adventure-16,Crafting-32
        var levelFrom = stream.ReadUInt32(); // поиск по уровню, от
        var levelTo = stream.ReadUInt32(); // поиск по уровню, до
        var name = stream.ReadString(); // поиск по имени, обычно пустой
        var sortType = stream.ReadByte(); // 1-registered, 2-Members,2-Level,3-name

        Logger.Debug("GetExpeditionMyRecruitments");

        ExpeditionManager.Instance.SendExpeditionMyRecruitmentsInfo(Connection, my, page, interest, levelFrom, levelTo, name, sortType);
    }
}
