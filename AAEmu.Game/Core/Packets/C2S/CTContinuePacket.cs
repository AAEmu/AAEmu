using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTContinuePacket : StreamPacket
    {
        public CTContinuePacket() : base(0x05)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadInt32();
            var next = stream.ReadInt32();
            StreamManager.Instance.ContinueCell(DbLoggerCategory.Database.Connection, id, next);
        }
    }
}