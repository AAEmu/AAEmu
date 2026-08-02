using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Team;

public class TeamMember(Character character = null) : PacketMarshaler
{
    public Character Character { get; set; } = character;
    public MemberRole Role { get; set; } = MemberRole.Undecided;
    public DiceBidRuleKind DiceBidRule { get; set; } = DiceBidRuleKind.Default;
    public bool DiceBidRuleChangedByIdleState { get; set; }
    public bool HasGoneRoundRobin { get; set; } = false;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Character.Id);
        stream.Write(Character.Name);
        stream.Write((byte)Character.Race);
        stream.Write((byte)Character.Gender);
        stream.Write(Character.Level);
        stream.Write((byte)Role);
        stream.WriteBc(Character.ObjId);
        return stream;
    }

    public PacketStream WritePerson(PacketStream stream)
    {
        // u64 type, i64 zi, i8 level, i8 heirLevel, PISC[4] hp/maxHp/mp/maxMp,
        // position, i8 ability[3], bool isOffline, i8 diceBidRule.
        stream.Write((ulong)Character.Id);
        stream.Write((long)Character.Transform.InstanceId);
        stream.Write((sbyte)Character.Level);
        stream.Write((sbyte)Character.HeirLevel);
        stream.WritePisc(
            (uint)Character.Hp,
            (uint)Character.MaxHp,
            (uint)Character.Mp,
            (uint)Character.MaxMp);
        stream.WritePosition(Character.Transform.World.Position);
        stream.Write((sbyte)Character.Ability1);
        stream.Write((sbyte)Character.Ability2);
        stream.Write((sbyte)Character.Ability3);
        stream.Write(!Character.IsOnline);
        stream.Write((sbyte)DiceBidRule);
        return stream;
    }
}
