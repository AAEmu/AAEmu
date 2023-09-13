using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpressEmotionPacket : GamePacket
    {
        public CSExpressEmotionPacket() : base(CSOffsets.CSExpressEmotionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();  // character
            var obj2Id = stream.ReadBc(); // target
            var emotionId = stream.ReadUInt32();

            _log.Warn("ExpressEmotion, ObjId: {0}, Obj2Id: {1}, EmotionId: {2}", objId, obj2Id, emotionId);
            Connection?.ActiveChar?.BroadcastPacket(new SCEmotionExpressedPacket(objId, obj2Id, emotionId), true);
            Connection?.ActiveChar?.Quests?.OnExpressFire(emotionId, objId, obj2Id);
        }
    }
}
