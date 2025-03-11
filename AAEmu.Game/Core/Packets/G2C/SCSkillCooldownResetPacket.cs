using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillCooldownResetPacket : GamePacket
{
    private Character _chr;
    private uint _skillId;
    private uint _tagId;
    private bool _gcd;
    private bool _rstc;
    private bool _rtsc;
    private bool _rtstc;

    public SCSkillCooldownResetPacket() : base(SCOffsets.SCSkillCooldownResetPacket, 5)
    {

    }

    public SCSkillCooldownResetPacket(Character chr, uint skillId, uint tagId, bool gcd, bool rstc = false, bool rtsc = false, bool rtstc = false) : base(SCOffsets.SCSkillCooldownResetPacket, 5)
    {
        _skillId = skillId;
        _tagId = tagId;
        _gcd = gcd;
        _chr = chr;
        _gcd = gcd;
        _rstc = rstc;
        _rtsc = rtsc;
        _rtstc = rtstc;
    }

    public override PacketStream Write(PacketStream stream)
    {
        //TODO заготовка для пакета

        stream.WriteBc(_chr.ObjId); // unitId
        stream.Write(_skillId); // skillId
        stream.Write(_tagId); // tagId
        stream.Write(_gcd); // gcd - Trigger GCD
        stream.Write(_gcd); // rstc
        stream.Write(_gcd); // rtsc
        stream.Write(_gcd); // rtstc

        return stream;
    }
}
