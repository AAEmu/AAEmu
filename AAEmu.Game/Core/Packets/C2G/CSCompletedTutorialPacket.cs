using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCompletedTutorialPacket : GamePacket
{
    public CSCompletedTutorialPacket() : base(CSOffsets.CSCompletedTutorialPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32();

        Logger.Debug($"SaveTutorial, Id: {id}");

        var completedQuestBlock = Connection.ActiveChar.Quests.SetCompletedQuestFlag(id, true);
        var body = new byte[8];
        completedQuestBlock.Body.CopyTo(body, 0);

        Connection.SendPacket(new SCTutorialCompletedPacket(id, body));
    }
}
