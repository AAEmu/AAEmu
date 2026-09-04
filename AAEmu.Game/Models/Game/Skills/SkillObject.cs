using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills;

public enum SkillObjectType
{
    None = 0,
    Unk1 = 1,
    Unk2 = 2,
    Unk3 = 3,
    Unk4 = 4,
    Unk5 = 5,
    Unk6 = 6,
    ItemGradeEnchantingSupport = 7,

    /// <summary>Synthesis material slots. See <see cref="SkillObjectItemEvolvingMaterials"/>.</summary>
    ItemEvolvingMaterials = 8,

    /// <summary>10.0.2.13 <c>ActiveAbilitySet</c> — payload is skillsaver slot index (i32).</summary>
    AbilitySet = 15,

    /// <summary>The awakening result the player picked. See <see cref="SkillObjectItemChangeMapping"/>.</summary>
    ItemChangeMapping = 26
}

public class SkillObject : PacketMarshaler
{
    public SkillObjectType Flag { get; set; } = SkillObjectType.None;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)Flag);
        return stream;
    }

    /// <summary>
    /// Whether this server can parse a skill-object type. Reading an unknown one would take the
    /// wrong number of bytes off the stream and desync everything after it, so callers drop those
    /// rather than guess.
    /// </summary>
    /// <summary>
    /// Whether the low bits of a cast's flag byte name a shape this server can read back.
    /// </summary>
    /// <remarks>
    /// Six is excluded. The flag is a bit mask, and only bit 3 carries a payload, so a cast setting
    /// bits 1 and 2 arrives as "6" without a body behind it - tempering is one. Read as a type it
    /// pulled a string out of the next field and echoed that back into the cast packet, which cost
    /// the cast its animation the same way an announced-but-missing body did for synthesis. Nothing
    /// in this build sends a genuine one.
    /// </remarks>
    public static bool IsKnownType(int flagType) =>
        flagType is >= (int)SkillObjectType.Unk1 and <= (int)SkillObjectType.ItemEvolvingMaterials
            and not (int)SkillObjectType.Unk6
            or (int)SkillObjectType.AbilitySet
            or (int)SkillObjectType.ItemChangeMapping;

    public static SkillObject GetByType(SkillObjectType flag)
    {
        SkillObject obj;
        switch (flag)
        {
            case SkillObjectType.Unk1: // TODO - Skills bound to portals
                obj = new SkillObjectUnk1();
                break;
            case SkillObjectType.Unk2: // TODO - Skills bound to home portals
                obj = new SkillObjectUnk2();
                break;
            case SkillObjectType.Unk3:
                obj = new SkillObjectUnk3();
                break;
            case SkillObjectType.Unk4:
                obj = new SkillObjectUnk4();
                break;
            case SkillObjectType.Unk5:
                obj = new SkillObjectUnk5();
                break;
            case SkillObjectType.Unk6:
                obj = new SkillObjectUnk6();
                break;
            case SkillObjectType.ItemGradeEnchantingSupport:
                obj = new SkillObjectItemGradeEnchantingSupport();
                break;
            case SkillObjectType.ItemEvolvingMaterials:
                obj = new SkillObjectItemEvolvingMaterials();
                break;
            case SkillObjectType.AbilitySet:
                obj = new SkillObjectAbilitySet();
                break;
            case SkillObjectType.ItemChangeMapping:
                obj = new SkillObjectItemChangeMapping();
                break;
            case SkillObjectType.None:
            default:
                obj = new SkillObject();
                break;
        }

        obj.Flag = flag;
        return obj;
    }
}

public class SkillObjectUnk1 : SkillObject
{
    public byte Type { get; set; }
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadByte();
        Id = stream.ReadInt32();
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongX(stream.ReadInt64());
        Z = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Type);
        stream.Write(Id);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongX(Y));
        stream.Write(Z);
        return stream;
    }
}

public class SkillObjectUnk2 : SkillObject
{
    public int Id { get; set; }
    public string Name { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadInt32();
        Name = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        stream.Write(Name);
        return stream;
    }
}

public class SkillObjectUnk3 : SkillObject
{
    public string Msg { get; set; }

    public override void Read(PacketStream stream)
    {
        Msg = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Msg);
        return stream;
    }
}

public class SkillObjectUnk4 : SkillObject
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void Read(PacketStream stream)
    {
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongY(stream.ReadInt64());
        Z = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongY(Y));
        stream.Write(Z);
        return stream;
    }
}

public class SkillObjectUnk5 : SkillObject
{
    public int Step { get; set; }

    public override void Read(PacketStream stream)
    {
        Step = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Step);
        return stream;
    }
}

public class SkillObjectUnk6 : SkillObject
{
    public string Name { get; set; }

    public override void Read(PacketStream stream)
    {
        Name = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Name);
        return stream;
    }
}

public class SkillObjectItemGradeEnchantingSupport : SkillObject
{
    public uint Id { get; set; }
    public ulong SupportItemId { get; set; }
    public bool AutoUseAaPoint { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadUInt32();
        SupportItemId = stream.ReadUInt64();
        AutoUseAaPoint = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        stream.Write(SupportItemId);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}

/// <summary>
/// The optional block that CSStartSkill's flag byte pulls in when bit 3 is set: thirteen ints,
/// named "v" in the original server's parser.
/// </summary>
/// <remarks>
/// The flag byte is a bit MASK in 10.0.2.13, not a SkillObjectType. Bits 0-2 are plain booleans
/// that pull no data; only bit 3 appends this block, and it arrives before inputDirection.
/// AAEmu read the byte as <c>flag &amp; 15</c>, so a real cast of flag 27 (0b11011) looked like
/// type 11, was clamped to None, and these thirteen ints were dropped on the floor - which is why
/// the dye colour never reached the server. Which of the thirteen carries what is not yet
/// established, so they are kept verbatim rather than named.
/// </remarks>
public class SkillObjectExtraValues : SkillObject
{
    public const int ValueCount = 13;

    public int[] Values { get; set; } = new int[ValueCount];

    /// <summary>How many of <see cref="Values"/> the sender actually supplied.</summary>
    public int ReadCount { get; private set; }

    public override void Read(PacketStream stream)
    {
        Values = new int[ValueCount];

        // Only as many as the sender supplied: this block is not always the full thirteen. A nest
        // interaction carries two, and reading the difference off the end of the body logged eleven
        // stream errors per cast while contributing nothing but zeroes.
        ReadCount = System.Math.Min(ValueCount, stream.LeftBytes / sizeof(int));
        for (var i = 0; i < ReadCount; i++)
            Values[i] = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        foreach (var value in Values)
            stream.Write(value);
        return stream;
    }
}

/// <summary>
/// The synthesis window's material slots, sent with the item synthesis skill.
/// </summary>
/// <remarks>
/// <para>Wire shape:</para>
/// <code>
/// u16  byteLength         // length of the id array in bytes, not a slot count
/// u64  materialItemId[byteLength / 8]
/// bool autoUseAaPoint     // pay the fee from AA points rather than coin
/// </code>
/// <para>
/// Confirmed against a live cast: a single Rank 1 infusion placed in the first slot arrives as
/// byteLength 48 (six slots) with the first id filled and the rest zero, and that id resolves to
/// the infusion sitting in the player's bag. The length the sender declares is authoritative, so
/// <see cref="MaterialSlots"/> is only the default the window offers - take
/// <see cref="UsedMaterialItemIds"/> instead of assuming a fixed array.
/// </para>
/// </remarks>
public class SkillObjectItemEvolvingMaterials : SkillObject
{
    /// <summary>Slots the synthesis window offers. The wire length wins over this.</summary>
    public const int MaterialSlots = 6;

    public ulong[] MaterialItemIds { get; set; } = new ulong[MaterialSlots];

    public bool AutoUseAaPoint { get; set; }

    /// <summary>The filled slots, in the order the client listed them.</summary>
    public IEnumerable<ulong> UsedMaterialItemIds => MaterialItemIds.Where(id => id != 0);

    public override void Read(PacketStream stream)
    {
        var byteLength = stream.ReadUInt16();
        var count = byteLength / sizeof(ulong);
        MaterialItemIds = new ulong[count];
        for (var i = 0; i < count; i++)
            MaterialItemIds[i] = stream.ReadUInt64();

        // A length that is not a whole number of ids would leave the stream mid-field; drop the
        // remainder so everything after the skill object stays aligned.
        for (var i = 0; i < byteLength % sizeof(ulong); i++)
            _ = stream.ReadByte();

        AutoUseAaPoint = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        var ids = MaterialItemIds ?? [];
        stream.Write((ushort)(ids.Length * sizeof(ulong)));
        foreach (var id in ids)
            stream.Write(id);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}

/// <summary>
/// The awakening result the player picked, sent with an awakening scroll's skill.
/// </summary>
/// <remarks>
/// A single <c>u32</c> naming an <c>item_change_mappings</c> row. It only matters for a
/// <c>selectable</c> group, where one source item and grade can lead to more than one result. The
/// server treats it as a request rather than an instruction - the awakening effect honours it only
/// when the row really belongs to the group the skill names and matches the target.
/// </remarks>
public class SkillObjectItemChangeMapping : SkillObject
{
    public uint MappingId { get; set; }

    public override void Read(PacketStream stream)
    {
        MappingId = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(MappingId);
        return stream;
    }
}
