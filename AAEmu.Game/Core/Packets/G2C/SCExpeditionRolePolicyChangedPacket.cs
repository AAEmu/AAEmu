using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionRolePolicyChangedPacket : GamePacket
    {
        private readonly ExpeditionRolePolicy _rolePolicy;
        private readonly bool _success;

        public SCExpeditionRolePolicyChangedPacket(ExpeditionRolePolicy rolePolicy, bool success) : base(0x00b, 1) // TODO 1.0 opcode: 0x00a
        {
            _rolePolicy = rolePolicy;
            _success = success;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_rolePolicy);
            return stream;
        }
    }
}
