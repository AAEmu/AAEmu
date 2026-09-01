using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// CS SkillCastExtra type 15 (<c>ActiveAbilitySet</c>). Payload is the skillsaver slot index as
/// <b>i16</b> (2 bytes). Reading it as i32 underruns when only those 2 bytes remain and
/// <see cref="PacketStream.ReadInt32"/> returns 0 — every activate looked like slot 0.
/// </summary>
public class SkillObjectAbilitySet : SkillObject
{
    public short SlotIndex { get; set; }

    public override void Read(PacketStream stream)
    {
        SlotIndex = stream.ReadInt16();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(SlotIndex);
        return stream;
    }
}
