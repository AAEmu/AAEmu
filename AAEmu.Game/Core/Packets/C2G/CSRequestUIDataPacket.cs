using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestUIDataPacket : GamePacket
    {
        public CSRequestUIDataPacket() : base(CSOffsets.CSRequestUIDataPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var uiDataType = stream.ReadUInt16();
            var id = stream.ReadUInt32();

            if (Connection.Characters.ContainsKey(id))
                Connection.SendPacket(
                    new SCResponseUIDataPacket(id, uiDataType, Connection.Characters[id].GetOption(uiDataType))
                );
        }
    }
}
