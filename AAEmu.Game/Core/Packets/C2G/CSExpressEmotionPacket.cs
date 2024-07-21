using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpressEmotionPacket : GamePacket
{
    public CSExpressEmotionPacket() : base(CSOffsets.CSExpressEmotionPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var characterObjId = stream.ReadBc();  // character
        var npcObjId = stream.ReadBc(); // target
        var emotionId = stream.ReadUInt32();

        Logger.Warn("ExpressEmotion, ObjId: {0}, Obj2Id: {1}, EmotionId: {2}", characterObjId, npcObjId, emotionId);
        Connection?.ActiveChar?.BroadcastPacket(new SCEmotionExpressedPacket(characterObjId, npcObjId, emotionId), true);

        //Connection?.ActiveChar?.Quests?.OnExpressFire(emotionId, characterObjId, npcObjId);
        // инициируем событие
        //Task.Run(() => QuestManager.Instance.DoOnExpressFireEvents(Connection.ActiveChar, emotionId, characterObjId, npcObjId));
        if (Connection != null)
        {
            var animId = ExpressTextManager.Instance.GetExpressAnimId(emotionId);
            QuestManager.Instance.DoOnExpressFireEvents(Connection.ActiveChar, animId, characterObjId, npcObjId);
        }
    }
}
