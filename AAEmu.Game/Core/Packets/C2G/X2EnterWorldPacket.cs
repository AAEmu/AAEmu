using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class X2EnterWorldPacket : GamePacket
    {
        uint[] nicIfTypes = new uint[10];

        public X2EnterWorldPacket() : base(CSOffsets.X2EnterWorldPacket, 1)
        {

        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var accountId = stream.ReadUInt64();
            var cookie = stream.ReadUInt32();
            var zoneId = stream.ReadUInt32();
            var tb = stream.ReadBoolean();
            var revision = stream.ReadUInt64();
            var index = stream.ReadByte();
            var ipAddr = stream.ReadUInt32();
            var machineId = stream.ReadString();
            for (int i = 0; i < 10; i++)
            {
                nicIfTypes[i] = stream.ReadUInt32();
            }
            var macError = stream.ReadUInt32();
            var immigrationHash = stream.ReadString();
            var passportKey = stream.ReadUInt32();
            var passportPass = stream.ReadUInt64();
            var is64bit = stream.ReadBoolean();

            EnterWorldManager.Instance.Login(Connection, accountId, cookie);
        }
    }
}
