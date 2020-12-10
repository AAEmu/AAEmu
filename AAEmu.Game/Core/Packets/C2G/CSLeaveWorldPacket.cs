using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveWorldPacket : GamePacket
    {
        public CSLeaveWorldPacket() : base(CSOffsets.CSLeaveWorldPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadByte();
            EnterWorldManager.Instance.Leave(Connection, type);
        }
    }
}
