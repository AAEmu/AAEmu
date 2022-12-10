using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills
{
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
        // added in 3+
        Unk8 = 8,
        Unk9 = 9,
        Unk10 = 0x0A,
        Unk11 = 0x0B,
        Unk12 = 0x0C,
        Unk13 = 0x0D,
        Unk14 = 0x0E,
        Unk15 = 0x0F,
        Unk16 = 0x10,
        Unk17 = 0x11,
        Unk18 = 0x12,
        Unk19 = 0x13
    }

    public class SkillObject : PacketMarshaler
    {
        public SkillObjectType Flag { get; set; } = SkillObjectType.None;

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)Flag);
            return stream;
        }

        public static SkillObject GetByType(SkillObjectType flag)
        {
            SkillObject obj;
            switch (flag)
            {
                case SkillObjectType.Unk1: // TODO - Skills bound to portals
                    {
                        obj = new SkillObjectUnk1();
                        break;
                    }
                case SkillObjectType.Unk2:
                    {
                        obj = new SkillObjectUnk2();
                        break;
                    }
                case SkillObjectType.Unk3:
                    {
                        obj = new SkillObjectUnk3();
                        break;
                    }
                case SkillObjectType.Unk4:
                    {
                        obj = new SkillObjectUnk4();
                        break;
                    }
                case SkillObjectType.Unk5:
                    {
                        obj = new SkillObjectUnk5();
                        break;
                    }
                case SkillObjectType.Unk6:
                    {
                        obj = new SkillObjectUnk6();
                        break;
                    }
                case SkillObjectType.ItemGradeEnchantingSupport:
                    obj = new SkillObjectItemGradeEnchantingSupport();
                    break;
                case SkillObjectType.Unk8:
                    obj = new SkillObjectUnk8(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk9:
                    obj = new SkillObjectUnk9(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk10:
                    obj = new SkillObjectUnk10(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk11:
                    obj = new SkillObjectUnk11(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk12:
                    obj = new SkillObjectUnk12(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk13:
                    obj = new SkillObjectUnk13(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk14:
                    obj = new SkillObjectUnk14(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk15:
                    obj = new SkillObjectUnk15(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk16:
                    obj = new SkillObjectUnk16(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk17:
                    obj = new SkillObjectUnk17(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk18:
                    obj = new SkillObjectUnk18(); // added in 3.5.0.3 NA
                    break;
                case SkillObjectType.Unk19:
                    obj = new SkillObjectUnk19(); // added in 3.5.0.3 NA
                    break;
                default:
                    {
                        obj = new SkillObject();
                        break;
                    }
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
        public int IndunZoneKey { get; set; } // added in 3+

        public override void Read(PacketStream stream)
        {
            Type = stream.ReadByte();
            Id = stream.ReadInt32();
            X = Helpers.ConvertLongX(stream.ReadInt64());
            Y = Helpers.ConvertLongX(stream.ReadInt64());
            Z = stream.ReadSingle();
            IndunZoneKey = stream.ReadInt32(); // added in 3+
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(Type);
            stream.Write(Id);
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongX(Y));
            stream.Write(Z);
            stream.Write(IndunZoneKey); // added in 3+

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
        public ulong SupportItemId { get; set; }
        public bool AutoUseAaPoint { get; set; }

        public override void Read(PacketStream stream)
        {
            SupportItemId = stream.ReadUInt64();
            AutoUseAaPoint = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(SupportItemId);
            stream.Write(AutoUseAaPoint);

            return stream;
        }
    }

    // all bottom added in 3+
    public class SkillObjectUnk8 : SkillObject
    {
        public byte Type { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Rot { get; set; }

        public override void Read(PacketStream stream)
        {
            Type = stream.ReadByte();
            X = Helpers.ConvertLongX(stream.ReadInt64());
            Y = Helpers.ConvertLongX(stream.ReadInt64());
            Z = stream.ReadSingle();
            Rot = stream.ReadSingle();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(Type);
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongX(Y));
            stream.Write(Z);
            stream.Write(Rot);

            return stream;
        }
    }

    public class SkillObjectUnk9 : SkillObject
    {
        public ulong M1ItemId { get; set; }
        public ulong M2ItemId { get; set; }
        public int ChangeIndex { get; set; }
        public bool AutoUseAAPoint { get; set; }

        public override void Read(PacketStream stream)
        {
            M1ItemId = stream.ReadUInt64();
            M2ItemId = stream.ReadUInt64();
            ChangeIndex = stream.ReadInt32();
            AutoUseAAPoint = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(M1ItemId);
            stream.Write(M2ItemId);
            stream.Write(ChangeIndex);
            stream.Write(AutoUseAAPoint);

            return stream;
        }
    }

    public class SkillObjectUnk10 : SkillObject
    {
        public int ChangeIndex { get; set; }

        public override void Read(PacketStream stream)
        {
            ChangeIndex = stream.ReadInt32();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(ChangeIndex);

            return stream;
        }
    }

    public class SkillObjectUnk11 : SkillObject
    {
        public bool AutoUseAAPoint { get; set; }
        public int Count { get; set; }
        public bool Continuous { get; set; }

        public override void Read(PacketStream stream)
        {
            AutoUseAAPoint = stream.ReadBoolean();
            Count = stream.ReadInt32();
            Continuous = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(AutoUseAAPoint);
            stream.Write(Count);
            stream.Write(Continuous);

            return stream;
        }
    }

    public class SkillObjectUnk12 : SkillObject
    {
        public int Index { get; set; }
        public bool IsAll { get; set; }

        public override void Read(PacketStream stream)
        {
            Index = stream.ReadInt32();
            IsAll = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(Index);
            stream.Write(IsAll);

            return stream;
        }
    }

    public class SkillObjectUnk13 : SkillObject
    {
        public int Count { get; set; }
        public uint Type { get; set; }

        public override void Read(PacketStream stream)
        {
            Count = stream.ReadInt32();
            for (var i = 0; i < 50; i++)
            {
                Type = stream.ReadUInt32();
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(Count);
            for (var i = 0; i < 50; i++)
            {
                stream.Write(Type);
            }

            return stream;
        }
    }

    public class SkillObjectUnk14 : SkillObject
    {
        public byte SlotIndex { get; set; }

        public override void Read(PacketStream stream)
        {
            SlotIndex = stream.ReadByte();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(SlotIndex);

            return stream;
        }
    }

    public class SkillObjectUnk15 : SkillObject
    {
        public int Count { get; set; }

        public override void Read(PacketStream stream)
        {
            Count = stream.ReadInt32();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(Count);

            return stream;
        }
    }

    public class SkillObjectUnk16 : SkillObject
    {
        public byte Package { get; set; }

        public override void Read(PacketStream stream)
        {
            Package = stream.ReadByte();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(Package);

            return stream;
        }
    }

    public class SkillObjectUnk17 : SkillObject
    {
        public byte ByProc { get; set; }

        public override void Read(PacketStream stream)
        {
            ByProc = stream.ReadByte();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(ByProc);

            return stream;
        }
    }

    public class SkillObjectUnk18 : SkillObject
    {
        public int Ability { get; set; }

        public override void Read(PacketStream stream)
        {
            Ability = stream.ReadInt32();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(Ability);

            return stream;
        }
    }

    public class SkillObjectUnk19 : SkillObject
    {
        public bool AutoUseAAPoint { get; set; }
        public int SmeltingDescId { get; set; }

        public override void Read(PacketStream stream)
        {
            AutoUseAAPoint = stream.ReadBoolean();
            SmeltingDescId = stream.ReadInt32();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write(AutoUseAAPoint);
            stream.Write(SmeltingDescId);

            return stream;
        }
    }
}
