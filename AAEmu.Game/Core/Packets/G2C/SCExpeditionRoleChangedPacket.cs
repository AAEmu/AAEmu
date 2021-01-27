using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionRoleChangedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly byte _role;
        private readonly string _charName;

        public SCExpeditionRoleChangedPacket(uint id, byte role, string charName) : base(SCOffsets.SCExpeditionRoleChangedPacket, 5)
        {
            _id = id;
            _role = role;
            _charName = charName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_role);
            stream.Write(_charName);
            return stream;
        }
    }
}
