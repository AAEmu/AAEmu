using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Core.Packets.S2C;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTItemUccPacket : StreamPacket
    {
        public CTItemUccPacket() : base(CTOffsets.CTItemUccPacket)
        {
        }

        public override void Read(PacketStream stream)
        {

            var player = stream.ReadUInt32();
            var count = stream.ReadUInt32();
            var items = new List<ulong>();
            for (var i = 0; i < count; i++)
                items.Add(stream.ReadUInt64());

            Connection.SendPacket(new TCItemUccDataPacket(player,count,items));
        }
    }
}
