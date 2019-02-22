using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionRolePolicyListPacket : GamePacket
    {
        private readonly ExpeditionRolePolicy[] _rolePolicies;
        
        public SCExpeditionRolePolicyListPacket(ExpeditionRolePolicy[] rolePolicies) : base(SCOffsets.SCExpeditionRolePolicyListPacket, 1)
        {
            _rolePolicies = rolePolicies;
        }

        public override PacketStream Write(PacketStream stream)
        {
            foreach (var rolePolicy in _rolePolicies) // TODO max length 20
                stream.Write(rolePolicy);
            return stream;
        }
    }
}
