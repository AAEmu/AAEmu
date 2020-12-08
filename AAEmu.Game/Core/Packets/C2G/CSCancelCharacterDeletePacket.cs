using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelCharacterDeletePacket : GamePacket
    {
        public CSCancelCharacterDeletePacket() : base(CSOffsets.CSCancelCharacterDeletePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterId = stream.ReadUInt32();
            Connection.SetRestoreCharacter(characterId);
        }
    }
}
