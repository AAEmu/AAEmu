using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Team;

public class TeamMember(Character character = null) : PacketMarshaler
{
    public Character Character { get; set; } = character;
    public MemberRole Role { get; set; } = MemberRole.Undecided;
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

    /// <summary>
    /// Writes a 50-byte "remote team member" snapshot used by SCTeamRemoteMembersExPacket
    /// and SCTeamMemberDisconnectedPacket. The ObjId is carried explicitly (0 when offline)
    /// so the client can correlate the member against units it has already learned about,
    /// even when the member is outside its render distance.
    /// </summary>
    public PacketStream WritePerson(PacketStream stream)
    {
        stream.Write(Character.Id);                                             // uint   (4)
        stream.Write(Character.Transform.ZoneId);                               // uint   (4) - zone id
        stream.Write(Character.Transform.InstanceId);                           // uint   (4) - instance id
        stream.Write(Character.Level);                                          // byte   (1)
        stream.Write(Character.Hp);                                             // int    (4)
        stream.Write(Character.MaxHp);                                          // int    (4)
        stream.Write(Character.Mp);                                             // int    (4)
        stream.Write(Character.MaxMp);                                          // int    (4)
        stream.WritePosition(Character.Transform.World.Position);               // bytes  (9)
        stream.WriteBc(Character.IsOnline ? Character.ObjId : 0);               // bc     (3) - ObjId or 0 if offline
        stream.Write((byte)0);                                                  // byte   (1) - padding
        stream.Write((byte)Character.Ability1);                                 // byte   (1)
        stream.Write((byte)Character.Ability2);                                 // byte   (1)
        stream.Write((byte)Character.Ability3);                                 // byte   (1)
        stream.Write((byte)0);                                                  // byte   (1)
        stream.Write((byte)0);                                                  // byte   (1)
        stream.Write((byte)0);                                                  // byte   (1)
        stream.Write((byte)0);                                                  // byte   (1)
        stream.Write((byte)0);                                                  // byte   (1)
        return stream;                                                          // total: 50 bytes
    }
}
