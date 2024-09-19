using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Team;

public class TeamMember : PacketMarshaler
{
    public Character Character { get; set; }
    public MemberRole Role { get; set; }
    public bool HasGoneRoundRobin { get; set; }

    public TeamMember(Character character = null)
    {
        Character = character;
        Role = MemberRole.Undecided;
        HasGoneRoundRobin = false;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Character.Id);
        stream.Write(Character.Name);
        stream.Write((byte)Character.Race);
        stream.Write((byte)Character.Gender);
        stream.Write(Character.Level);
        stream.Write((byte)Role);
        stream.WriteBc(Character.ObjId); // uid
        stream.Write(Character.HeirLevel);
        return stream;
    }

    public PacketStream WritePerson(PacketStream stream)
    {
        stream.Write(Character.Id);
        stream.Write((ulong)0); // zi
        stream.Write(Character.Level);
        stream.Write(Character.HeirLevel);
        stream.WritePisc(Character.Hp, Character.MaxHp, Character.Mp, Character.MaxMp);
        //stream.Write(Character.Hp);
        //stream.Write(Character.MaxHp);
        //stream.Write(Character.Mp);
        //stream.Write(Character.MaxMp);
        stream.WritePosition(Character.Transform.World.Position);
        //stream.Write((double)(Character.Transform.World.Rotation.Z).RadToDeg()); // angZ
        stream.Write((byte)Character.Ability1);
        stream.Write((byte)Character.Ability2);
        stream.Write((byte)Character.Ability3);
        stream.Write((byte)Character.HighAbility1); // highAbility
        stream.Write((byte)Character.HighAbility2); // highAbility
        stream.Write((byte)Character.HighAbility3); // highAbility
        stream.Write(!Character.IsOnline); // isOffline
        stream.Write(HasGoneRoundRobin); // diceBidRule
        return stream;
    }
}
