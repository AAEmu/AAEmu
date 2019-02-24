using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionRolePolicyChangedPacket : GamePacket
    {
        private readonly ExpeditionRolePolicy _rolePolicy;

        public SCExpeditionRolePolicyChangedPacket(ExpeditionRolePolicy rolePolicy) : base(0x00a, 1)
        {
            _rolePolicy = rolePolicy;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_rolePolicy);
            return stream;
        }
    }
}
