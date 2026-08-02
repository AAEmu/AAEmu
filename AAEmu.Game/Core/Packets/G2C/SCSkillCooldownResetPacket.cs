using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: bc, i32 skill, i32 tag, then the four flags gc, rstc, rtsc
/// and rtstc — which is what this writes.
/// </remarks>
public class SCSkillCooldownResetPacket : GamePacket
{
    private readonly Character _chr;
    private readonly uint _skillId;
    private readonly uint _tagId;
    private readonly bool _gcd;
    private readonly bool _resetSkillTagCooldown;
    private readonly bool _resetToggleSkillCooldown;
    private readonly bool _resetToggleSkillTagCooldown;

    public SCSkillCooldownResetPacket() : base(SCOffsets.SCSkillCooldownResetPacket, 1)
    {

    }

    public SCSkillCooldownResetPacket(
        Character chr, uint skillId, uint tagId, bool gcd,
        bool resetSkillTagCooldown = false,
        bool resetToggleSkillCooldown = false,
        bool resetToggleSkillTagCooldown = false)
        : base(SCOffsets.SCSkillCooldownResetPacket, 1)
    {
        _chr = chr;
        _skillId = skillId;
        _tagId = tagId;
        _gcd = gcd;
        _resetSkillTagCooldown = resetSkillTagCooldown;
        _resetToggleSkillCooldown = resetToggleSkillCooldown;
        _resetToggleSkillTagCooldown = resetToggleSkillTagCooldown;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_chr.ObjId);
        stream.Write(_skillId);
        stream.Write(_tagId);
        stream.Write(_gcd);
        // 10.0.2.13 reads three further flags after gcd; sending only gcd left the client taking
        // the following packet's bytes as this one's tail.
        stream.Write(_resetSkillTagCooldown);
        stream.Write(_resetToggleSkillCooldown);
        stream.Write(_resetToggleSkillTagCooldown);
        return stream;
    }
}
