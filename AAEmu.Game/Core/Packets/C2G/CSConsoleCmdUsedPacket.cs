using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConsoleCmdUsedPacket : GamePacket
    {
        public CSConsoleCmdUsedPacket() : base(CSOffsets.CSConsoleCmdUsedPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var cmd = stream.ReadString();

            _log.Debug("ConsoleCmdUsed, Cmd: {0}", cmd);
        }
    }
}
