using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBroadcastVisualOptionPacket : GamePacket
    {
        public CSBroadcastVisualOptionPacket() : base(CSOffsets.CSBroadcastVisualOptionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            Connection.ActiveChar.VisualOptions.Read(stream);

            Connection.ActiveChar.BroadcastPacket(
                new SCUnitVisualOptionsPacket(Connection.ActiveChar.ObjId, Connection.ActiveChar.VisualOptions), true);
        }
    }
}
