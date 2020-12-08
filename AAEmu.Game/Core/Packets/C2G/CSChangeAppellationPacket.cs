using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeAppellationPacket : GamePacket
    {
        public CSChangeAppellationPacket() : base(CSOffsets.CSChangeAppellationPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            Connection.ActiveChar.Appellations.Change(id);
        }
    }
}
