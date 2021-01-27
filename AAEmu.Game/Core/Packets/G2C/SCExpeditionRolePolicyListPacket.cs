using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionRolePolicyListPacket : GamePacket
    {
        private readonly List<ExpeditionRolePolicy> _rolePolicies;

        public SCExpeditionRolePolicyListPacket(List<ExpeditionRolePolicy> rolePolicies) : base(SCOffsets.SCExpeditionRolePolicyListPacket, 5)
        {
            _rolePolicies = rolePolicies;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_rolePolicies.Count);  // count Byte
            foreach (var rolePolicy in _rolePolicies) // TODO in 1.2 max length 20
            {
                stream.Write(rolePolicy);
            }

            return stream;
        }
    }
}
