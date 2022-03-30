using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthGameOnPacket : LoginPacket
    {
        public CARequestAuthGameOnPacket() : base(CLOffsets.CARequestAuthGameOnPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var dev = stream.ReadBoolean();
            var qqno = stream.ReadUInt32();
            var len = stream.ReadUInt16();
            var sig = stream.ReadBytes(128); // length 128
            var key = stream.ReadBytes(16); // length 16
            var mac = stream.ReadBytes(8); // length 8
            var worldId = stream.ReadByte();
            var netbarSig = stream.ReadBytes(128); // length 128
            var is64bit = stream.ReadBoolean(); // added 5.7.5.0
        }
    }
}
