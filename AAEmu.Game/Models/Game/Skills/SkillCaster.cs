using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Skills;

public enum SkillCasterType : byte
{
    Unit = 0,
    Doodad = 1, // Doodad
    Item = 2,
    Mount = 3, // TODO mountSkillType
    Gimmick = 4 // Gimmick
}

public abstract class SkillCaster : PacketMarshaler
{
    public SkillCasterType Type { get; set; }
    public uint ObjId { get; set; }

    public override void Read(PacketStream stream)
    {
        ObjId = stream.ReadBc();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)Type); // skillCasterType
        stream.WriteBc(ObjId);
        return stream;
    }

    public static SkillCaster GetByType(SkillCasterType type)
    {
        SkillCaster obj;
        switch (type)
        {
            case SkillCasterType.Unit:
                obj = new SkillCasterUnit();
                break;
            case SkillCasterType.Doodad:
                obj = new SkillCasterDoodad();
                break;
            case SkillCasterType.Item:
                obj = new SkillItem();
                break;
            case SkillCasterType.Mount:
                obj = new SkillCasterMount();
                break;
            case SkillCasterType.Gimmick:
                obj = new SkillCasterGimmik();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        obj.Type = type;
        return obj;
    }
}

public class SkillCasterUnit : SkillCaster
{
    public SkillCasterUnit()
    {
    }

    public SkillCasterUnit(uint objId)
    {
        Type = SkillCasterType.Unit;
        ObjId = objId;
    }
}

public class SkillCasterDoodad : SkillCaster
{
    public SkillCasterDoodad()
    {
    }

    public SkillCasterDoodad(uint objId)
    {
        Type = SkillCasterType.Doodad;
        ObjId = objId;
    }
}

public class SkillItem : SkillCaster
{
    private ulong _itemId;
    public ulong ItemId
    {
        get => _itemId;
        set
        {
            if (_itemId == value)
                return;
            _itemId = value;
            if (_itemId > 0)
            {
                SkillSourceItem = ItemManager.Instance.GetItemByItemId(value);
                ItemTemplateId = SkillSourceItem?.TemplateId ?? 0;
            }
        }
    }

    public uint ItemTemplateId { get; set; }
    public byte Type1 { get; set; }
    public uint Type2 { get; set; }
    public Item SkillSourceItem { get; private set; }

    public SkillItem()
    {
    }

    public SkillItem(uint objId, ulong itemId, uint itemTemplateId)
    {
        Type = SkillCasterType.Item;
        ObjId = objId;
        ItemId = itemId;
        ItemTemplateId = itemTemplateId;
    }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        ItemId = stream.ReadUInt64();
        ItemTemplateId = stream.ReadUInt32();
        Type1 = stream.ReadByte();
        Type2 = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(ItemId);
        stream.Write(ItemTemplateId);
        stream.Write(Type1);
        stream.Write(Type2);
        return stream;
    }
}

public class SkillCasterMount : SkillCaster
{
    public uint MountSkillTemplateId { get; set; }

    public SkillCasterMount()
    {
    }

    public SkillCasterMount(uint objId)
    {
        Type = SkillCasterType.Mount;
        ObjId = objId;
    }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        MountSkillTemplateId = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(MountSkillTemplateId);
        return stream;
    }
}

public class SkillCasterGimmik : SkillCaster
{
    public SkillCasterGimmik()
    {
    }

    public SkillCasterGimmik(uint objId)
    {
        Type = SkillCasterType.Gimmick;
        ObjId = objId;
    }
}
