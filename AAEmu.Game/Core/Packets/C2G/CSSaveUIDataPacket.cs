using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSaveUIDataPacket : GamePacket
    {
        public CSSaveUIDataPacket() : base(CSOffsets.CSSaveUIDataPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var uiDataType = stream.ReadUInt16();
            var id = stream.ReadUInt32();
            var data = stream.ReadString();

            Connection.ActiveChar.SetOption(uiDataType, data);
        }
    }
}
