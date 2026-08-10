using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDropQuestContextPacket() : GamePacket(CSOffsets.CSDropQuestContextPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var questId = stream.ReadUInt32();
        Connection.ActiveChar.Quests.DropQuest(questId, true);
        Connection.ActiveChar.Quests.Send();
    }
}
