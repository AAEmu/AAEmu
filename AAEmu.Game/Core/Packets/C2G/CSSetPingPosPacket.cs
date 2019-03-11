using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetPingPosPacket : GamePacket
    {
        public CSSetPingPosPacket() : base(0x087, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var hasPing = stream.ReadBoolean();
            // TODO check if this is how you properly read position.
            // a2->Reader->ReadPos("pos", v2 + 20, 0);
            var x = stream.ReadSingle();
            var y = stream.ReadSingle();
            var z = stream.ReadSingle();
            var insId = stream.ReadUInt32();
            _log.Warn("SetPingPos");
        }
    }
}
