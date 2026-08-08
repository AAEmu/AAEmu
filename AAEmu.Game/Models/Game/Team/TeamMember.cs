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

    /// <summary>
    /// One team member, read out of the client's own deserializer (VA 0x39C7C570). The SAME function is
    /// called by SCJoinedTeam (once per member) and by SCTeamMemberJoined, so both packets share this
    /// layout:
    ///
    ///   type        u64      8    the character id - EIGHT bytes, not four
    ///   name        wstring
    ///   CharRace    u8       1
    ///   CharGender  u8       1
    ///   level       u8       1
    ///   role        u8       1
    ///   uid         bc       3    the object id
    ///   heirLevel   u8       1
    ///
    /// The id was written as u32 and heirLevel was missing entirely, which shifted every field after
    /// the id by four bytes and left the member one byte short.
    /// </summary>
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)Character.Id);      // u64     type
        stream.Write(Character.Name);           // wstring name
        stream.Write((byte)Character.Race);     // u8      CharRace
        stream.Write((byte)Character.Gender);   // u8      CharGender
        stream.Write(Character.Level);          // u8      level
        stream.Write((byte)Role);               // u8      role
        stream.WriteBc(Character.ObjId);        // bc      uid
        stream.Write(Character.HeirLevel);      // u8      heirLevel
        return stream;
    }

    public PacketStream WritePerson(PacketStream stream)
    {
        // u64 type, i64 zi, i8 level, i8 heirLevel, PISC[4] hp/maxHp/mp/maxMp,
        // position, i8 ability[3], bool isOffline, i8 diceBidRule.
        stream.Write((ulong)Character.Id);
        // "zi" is the ZONE id, not the instance id. We used to send Transform.InstanceId here, which is 0
        // in the main world, and the client read that as "no map": a party member's marker disappeared
        // the moment they left visual range, because only then does the client need this field to place
        // them. Everything else in the packet was already correct - the coordinates round-trip through
        // the client's own decoder to within 2 mm, and HP/MP from the same packet always worked.
        stream.Write((long)Character.Transform.ZoneId);
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
