using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCUnitDamagedPacket(
    CastAction castAction,
    SkillCaster skillCaster,
    uint casterId,
    uint targetId,
    int damage,
    int absorbed)
    : GamePacket(SCOffsets.SCUnitDamagedPacket, 1)
{
    public int ManaBurn { get; set; }

    /// <summary>Legacy alias.</summary>
    public int _manaBurn
    {
        get => ManaBurn;
        set => ManaBurn = value;
    }

    public byte HoldableId { get; set; }
    public SkillHitType HitType { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        return UnitDamagedWire.Write(
            stream,
            castAction,
            skillCaster,
            casterId,
            targetId,
            damage,
            absorbed,
            ManaBurn,
            HoldableId,
            HitType);
    }
}
