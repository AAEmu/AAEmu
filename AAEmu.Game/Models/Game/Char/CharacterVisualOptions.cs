using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterVisualOptions : PacketMarshaler
    {
        private byte _flag;
        public byte[] Stp;
        public bool Helmet;
        public bool BackHoldable;
        public bool Cosplay;
        public bool CosplayBackpack;
        public bool CosplayVisual;

        public override void Read(PacketStream stream)
        {
            _flag = stream.ReadByte();
            if ((_flag & 1) == 1)
                Stp = stream.ReadBytes(6);
            if ((_flag & 2) == 2)
                Helmet = stream.ReadBoolean();
            if ((_flag & 4) == 4)
                BackHoldable = stream.ReadBoolean();
            if ((_flag & 8) == 8)
                Cosplay = stream.ReadBoolean();
            if ((_flag & 0x10) == 0x10)
                CosplayBackpack = stream.ReadBoolean();
            if ((_flag & 0x20) == 0x20)
                CosplayVisual = stream.ReadBoolean(); // cosplay_visual, added in 1.7
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_flag);
            return Write(stream, _flag);
        }

        public PacketStream Write(PacketStream stream, byte flag)
        {
            if ((flag & 1) == 1)
                stream.Write(Stp);
            if ((flag & 2) == 2)
                stream.Write(Helmet);
            if ((flag & 4) == 4)
                stream.Write(BackHoldable);
            if ((flag & 8) == 8)
                stream.Write(Cosplay);
            if ((flag & 0x10) == 0x10)
                stream.Write(CosplayBackpack);
            if ((flag & 0x20) == 0x20)
                stream.Write(CosplayVisual); // cosplay_visual, added in 1.7

            return stream;
        }
        public PacketStream WriteOptions(PacketStream stream)
        {
            // all this data must be output to the SCUnitStatePacket
            stream.Write(Stp);             // stp
            stream.Write(Helmet);          // helmet
            stream.Write(BackHoldable);    // back_holdable
            stream.Write(Cosplay);         // cosplay
            stream.Write(CosplayBackpack); // cosplay_backpack
            stream.Write(CosplayVisual);   // cosplay_visual, added in 1.7

            return stream;
        }
    }
}
