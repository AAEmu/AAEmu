using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakeAttachmentItemPacket : GamePacket
    {
        public CSTakeAttachmentItemPacket() : base(0x09d, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            var templateId = stream.ReadUInt32();
            if (templateId > 0) // TODO item read
            {
                var id = stream.ReadUInt64();
                var type = stream.ReadByte(); // TODO а нужен ли весь остальной хлам?
                var flags = stream.ReadByte();
                var count = stream.ReadInt32();
                var detailType = stream.ReadByte();
                var detailLength = 0;
                switch (detailType)
                {
                    case 1:
                        detailLength = 52;
                        break;
                    case 2:
                        detailLength = 30;
                        break;
                    case 3:
                        detailLength = 7;
                        break;
                    case 4:
                        detailLength = 10;
                        break;
                    case 5:
                        detailLength = 25;
                        break;
                    case 6:
                    case 7:
                        detailLength = 17;
                        break;
                    case 8:
                        detailLength = 9;
                        break;
                }

                if (detailLength > 0)
                    stream.ReadBytes(detailLength); // detail

                var creationTime = stream.ReadDateTime();
                var lifespanMins = stream.ReadUInt32();
                stream.ReadUInt32(); // type(id)
                var worldId = stream.ReadByte();
                var unsecureDateTime = stream.ReadDateTime();
                var unpackDateTime = stream.ReadDateTime();
            }

            stream.ReadByte();
            var slotType = stream.ReadByte();
            stream.ReadByte();
            var slot = stream.ReadByte();

            _log.Debug("TakeAttachmentItem");
        }
    }
}
