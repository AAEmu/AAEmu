using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class X2EnterWorldPacket : GamePacket
    {
        public X2EnterWorldPacket() : base(0x000, 1)
        {

        }

        public override void Read(PacketStream stream)
        {
            stream.ReadUInt32(); // pFrom
            stream.ReadUInt32(); // pTo
            var accountId = stream.ReadUInt32();
            var cookie = stream.ReadUInt32();
            stream.ReadInt32(); // zoneId
            stream.ReadByte(); // tb
            stream.ReadUInt64(); // revision

            EnterWorldManager.Instance.Login(Connection, accountId, cookie);
        }
    }
}