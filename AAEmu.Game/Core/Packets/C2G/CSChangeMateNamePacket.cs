using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeMateNamePacket : GamePacket
    {
        public CSChangeMateNamePacket() : base(CSOffsets.CSChangeMateNamePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();
            var name = stream.ReadString();

            //_log.Warn("ChangeMateName, TlId: {0}, Name: {1}", tlId, name);
            MateManager.Instance.RenameMount(Connection, tlId, name);
        }
    }
}
