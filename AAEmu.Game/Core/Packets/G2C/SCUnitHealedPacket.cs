using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitHealedPacket : GamePacket
{
    private readonly CastAction _castAction;
    private readonly SkillCaster _skillCaster;
    private readonly uint _targetId;
    private readonly HealType _healType;
    private readonly HealHitType _healHitType;
    private readonly int _value;

    public SCUnitHealedPacket(CastAction castAction, SkillCaster skillCaster, uint targetId, HealType healType, HealHitType healHitType, int value)
        : base(SCOffsets.SCUnitHealedPacket, 1)
    {
        _castAction = castAction;
        _skillCaster = skillCaster;
        _targetId = targetId;
        _healType = healType;
        _healHitType = healHitType;
        _value = value;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_castAction);
        stream.Write(_skillCaster);
        stream.WriteBc(_targetId);
        stream.Write((byte)_healType); // h
        stream.Write((byte)_healHitType); // h
        stream.Write(_value); // a
        stream.Write(0); // o
        stream.Write((byte)1); // result -> to debug into
        return stream;
    }
}
