using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitHealedPacket(
    CastAction castAction,
    SkillCaster skillCaster,
    uint targetId,
    HealType healType,
    HealHitType healHitType,
    int value)
    : GamePacket(SCOffsets.SCUnitHealedPacket, 1)
{
    public int Overheal { get; set; }
    public byte CrimeState { get; set; }
    public uint ElementHeal { get; set; }
    public bool ShowElementEffect { get; set; }
    public uint ElementType { get; set; }
    public byte Result { get; set; } = 1;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(castAction);
        stream.Write(skillCaster);
        stream.WriteBc(targetId);
        stream.Write((byte)healType);
        stream.Write((byte)healHitType);
        // Client reads "a" and "o" via the 8-byte archive slot (not s32).
        stream.Write((long)value);
        stream.Write((long)Overheal);
        stream.Write(CrimeState);
        stream.Write(ElementHeal);
        stream.Write(ShowElementEffect);
        stream.Write(ElementType);
        stream.Write(Result);
        return stream;
    }
}
