using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Combat resource total for a unit — what drives the combo-pip UI.
/// </summary>
/// <remarks>
/// Body: bc unit, i32 resource type, u64 point, i32 updateTime. <paramref name="point"/> is already in
/// client centi-points (<see cref="AAEmu.Game.Models.Game.Skills.CombatResourceRules.ToWirePoint"/>).
/// <paramref name="updateTime"/> is elapsed ms into <c>recovery_cycle</c> (0 restarts the HUD timer);
/// see <see cref="AAEmu.Game.Models.Game.Skills.CombatResourceRules.ToWireUpdateTime"/>.
/// </remarks>
public class SCCombatResourcePointPacket(uint objId, int combatResourceId, ulong point, int updateTime)
    : GamePacket(SCOffsets.SCCombatResourcePointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(combatResourceId);
        stream.Write(point);
        stream.Write(updateTime);
        return stream;
    }
}
