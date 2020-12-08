using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeleteCharacterPacket : GamePacket
    {
        public CSDeleteCharacterPacket() : base(CSOffsets.CSDeleteCharacterPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterId = stream.ReadUInt32();
            Connection.SetDeleteCharacter(characterId);
        }
    }
}
