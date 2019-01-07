using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestUIDataPacket : GamePacket
    {
        public CSRequestUIDataPacket() : base(0x113, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var uiDataKey = stream.ReadString();
            var id = stream.ReadUInt32();
            var name = stream.ReadString();

            _log.Info("RequestUIData: {0}, {1}", uiDataKey, name);
        }
    }
}