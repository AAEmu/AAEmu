using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRestrictCheckPacket : GamePacket
    {
        public CSRestrictCheckPacket() : base(CSOffsets.CSRestrictCheckPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterId = stream.ReadUInt32();
            var code = stream.ReadByte();
            Connection.SendPacket(new SCResultRestrictCheckPacket(characterId, code, 0));
        }
    }
}
