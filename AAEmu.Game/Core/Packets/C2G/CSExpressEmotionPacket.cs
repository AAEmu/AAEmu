using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpressEmotionPacket : GamePacket
    {
        public CSExpressEmotionPacket() : base(0x0ad, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var obj2Id = stream.ReadBc();
            var emotionId = stream.ReadUInt32();

            //_log.Warn("ExpressEmotion, ObjId: {0}, Obj2Id: {1}, EmotionId: {2}", objId, obj2Id, emotionId);
            // TODO - verify ids
            Connection.ActiveChar.BroadcastPacket(new SCEmotionExpressedPacket(objId, obj2Id, emotionId), true);
        }
    }
}
