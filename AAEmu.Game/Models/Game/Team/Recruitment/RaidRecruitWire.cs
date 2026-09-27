using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Team.Recruitment;

/// <summary>
/// The wire shapes the 10.0.2.13 client reads and writes for the board, taken from its own serializers:
/// the record, the applicant row and the detail.
/// Field names are the literals those functions pass; widths are their stream vtable slots.
/// </summary>
public static class RaidRecruitWire
{
    /// <summary>
    /// u64 type (owner id), string ownerName, i8 ownerLevel, i8 ownerAbility x3, i32 type (expedition id,
    /// the client resolves it to ownerExpedition), i32 type (unnamed, unread), i32 type, i32 subType,
    /// u32 headcount, u32 limitLevel, u32 limitGearPoint, bool autoJoin, string msg, u32 hour, u32 minute,
    /// i32 applicantCount, i32 memberCount, i32 leadershipPoint, i32 gearPoint, i64 createTime,
    /// i64 expireTime, i64 addExpireTime.
    /// </summary>
    public static PacketStream WriteRecord(PacketStream stream, in RaidRecruitRecord record)
    {
        stream.Write(record.OwnerId);
        stream.Write(record.OwnerName);
        stream.Write((sbyte)record.OwnerLevel);
        stream.Write((sbyte)record.OwnerAbility1);
        stream.Write((sbyte)record.OwnerAbility2);
        stream.Write((sbyte)record.OwnerAbility3);
        stream.Write(record.OwnerExpeditionId);
        stream.Write(0); // +0x9c, no name and no reader
        stream.Write(record.TypeId);
        stream.Write(record.SubTypeId);
        stream.Write(record.Headcount);
        stream.Write(record.LimitLevel);
        stream.Write(record.LimitGearPoint);
        stream.Write(record.AutoJoin);
        stream.Write(record.Message);
        stream.Write(record.Hour);
        stream.Write(record.Minute);
        stream.Write(record.ApplicantCount);
        stream.Write(record.MemberCount);
        stream.Write(record.LeadershipPoint);
        stream.Write(record.GearPoint);
        stream.Write(record.CreateTime);
        stream.Write(record.ExpireTime);
        stream.Write(record.AddExpireTime);
        return stream;
    }

    public static RaidRecruitRecord ReadRecord(PacketStream stream)
    {
        var ownerId = stream.ReadUInt64();
        var ownerName = stream.ReadString();
        var ownerLevel = (byte)stream.ReadSByte();
        var ability1 = (byte)stream.ReadSByte();
        var ability2 = (byte)stream.ReadSByte();
        var ability3 = (byte)stream.ReadSByte();
        var ownerExpeditionId = stream.ReadInt32();
        stream.ReadInt32(); // +0x9c
        var typeId = stream.ReadInt32();
        var subTypeId = stream.ReadInt32();
        var headcount = stream.ReadUInt32();
        var limitLevel = stream.ReadUInt32();
        var limitGearPoint = stream.ReadUInt32();
        var autoJoin = stream.ReadBoolean();
        var message = stream.ReadString();
        var hour = stream.ReadUInt32();
        var minute = stream.ReadUInt32();
        var applicantCount = stream.ReadInt32();
        var memberCount = stream.ReadInt32();
        var leadershipPoint = stream.ReadInt32();
        var gearPoint = stream.ReadInt32();
        var createTime = stream.ReadInt64();
        var expireTime = stream.ReadInt64();
        var addExpireTime = stream.ReadInt64();
        return new RaidRecruitRecord(ownerId, ownerName, ownerLevel, ability1, ability2, ability3,
            ownerExpeditionId, typeId, subTypeId, headcount, limitLevel, limitGearPoint, autoJoin, message,
            hour, minute, applicantCount, memberCount, leadershipPoint, gearPoint, createTime, expireTime,
            addExpireTime);
    }

    /// <summary>u64 type (character id), string charName, i8 level, i8 ability x3, u32 role, i32 gearPoint.</summary>
    public static PacketStream WriteApplicant(PacketStream stream, in RaidApplicantRecord applicant)
    {
        stream.Write(applicant.CharacterId);
        stream.Write(applicant.Name);
        stream.Write((sbyte)applicant.Level);
        stream.Write((sbyte)applicant.Ability1);
        stream.Write((sbyte)applicant.Ability2);
        stream.Write((sbyte)applicant.Ability3);
        stream.Write(applicant.Role);
        stream.Write(applicant.GearPoint);
        return stream;
    }

    public static RaidApplicantRecord ReadApplicant(PacketStream stream)
    {
        var characterId = stream.ReadUInt64();
        var name = stream.ReadString();
        var level = (byte)stream.ReadSByte();
        var ability1 = (byte)stream.ReadSByte();
        var ability2 = (byte)stream.ReadSByte();
        var ability3 = (byte)stream.ReadSByte();
        var role = stream.ReadUInt32();
        var gearPoint = stream.ReadInt32();
        return new RaidApplicantRecord(characterId, name, level, ability1, ability2, ability3, role, gearPoint);
    }

    /// <summary>
    /// SCRaidRecruitDetail: u64 type (owner id), string ownerName, i8 ownerLevel, i32 type
    /// (expedition id, reads +0x9c as ownerExpedition), i32 type, i32 subType, u32 limitLevel,
    /// u32 limitGearPoint, string msg, u32 hour, u32 minute, i64 createTime.
    /// </summary>
    public static PacketStream WriteDetail(PacketStream stream, in RaidRecruitRecord record)
    {
        stream.Write(record.OwnerId);
        stream.Write(record.OwnerName);
        stream.Write((sbyte)record.OwnerLevel);
        stream.Write(record.OwnerExpeditionId);
        stream.Write(record.TypeId);
        stream.Write(record.SubTypeId);
        stream.Write(record.LimitLevel);
        stream.Write(record.LimitGearPoint);
        stream.Write(record.Message);
        stream.Write(record.Hour);
        stream.Write(record.Minute);
        stream.Write(record.CreateTime);
        return stream;
    }
}
