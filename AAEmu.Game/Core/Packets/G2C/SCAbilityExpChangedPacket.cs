using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Applies a signed experience delta to one ability, or to active/all abilities when
/// <paramref name="ability"/> is <see cref="AbilityType.None"/>.
/// </summary>
/// <remarks>
/// Its two apparent i8 calls are mutually exclusive serializer read/write branches for the same
/// isApplyAll is consulted only for the AbilityType.None sentinel.
/// </remarks>
public class SCAbilityExpChangedPacket(uint objId, AbilityType ability, int exp, bool isApplyAll = false)
    : GamePacket(SCOffsets.SCAbilityExpChangedPacket, 1)
{
    private readonly sbyte _ability = (sbyte)ability;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(_ability);
        stream.Write(exp);
        stream.Write(isApplyAll);
        return stream;
    }
}
