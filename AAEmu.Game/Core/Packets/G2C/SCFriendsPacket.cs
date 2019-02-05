using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFriendsPacket : GamePacket
    {
        public SCFriendsPacket() : base(0x049, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(0); // total
            stream.Write(0); // count // TODO max count 200
            // TODO if count > 0
//            stream.Write(1u); // characterId
//            stream.Write("Testfriend"); // name
//            stream.Write((byte) 1); // charRace
//            stream.Write((byte) 25); // level
//            stream.Write(255); // health
//            stream.Write((byte) 1); // ability1
//            stream.Write((byte) 2); // ability2
//            stream.Write((byte) 3); // ability3
//            stream.Write(Helpers.ConvertLongX(0f)); // x
//            stream.Write(Helpers.ConvertLongY(0f)); // y
//            stream.Write(0f); // z
//            stream.Write(0); // zoneId
//            stream.Write(0u); // type(id)
//            stream.Write(false); // isParty
//            stream.Write(false); // isOnline
//            stream.Write(DateTime.Now); // lastWorldLeaveTime
            return stream;
        }
    }
}