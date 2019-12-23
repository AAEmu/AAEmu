using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNoticeMessagePacket : GamePacket
    {

        //Initialize
        readonly string _message = "";
        readonly byte _type = 3;
        readonly string _color = "9";
        readonly int _vistime = 1000;

        public SCNoticeMessagePacket(byte type, string color, int vistime, string message) : base(SCOffsets.SCNoticeMessagePacket, 1)
        {
            _type = type;
            _color = color;
            _vistime = vistime;
            _message = message;


        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_type); // noticeType (1 - 3)
            stream.Write(_color); // colorStr (Opacity 0 - 9)
            stream.Write(_vistime); // visibleTime (Miliseconds)
            stream.Write(_message); // message 

            return stream;
        }
    }
}
