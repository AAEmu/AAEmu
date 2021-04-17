using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Core.Packets.S2C;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTQueryCharNamePacket : StreamPacket
    {
        public CTQueryCharNamePacket() : base(CTOffsets.CTQueryCharNamePacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            var name = NameManager.Instance.GetCharacterName(id);
            if (name != null)
                Connection.SendPacket(new TCCharNameQueriedPacket(id, name));

            _log.Debug("QueryCharName, Id: {0}, Name: {1}", id, name);
        }
    }
}
