using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Basic player character data
/// </summary>
public partial class Character
{
    public uint Id { get; set; }

    public uint AccountId { get; set; }

    public string Name { get; set; }

    public uint AccessLevel { get; set; }

    public sbyte Race { get; set; }

    public bool Gender { get; set; }

    public byte[] UnitModelParams { get; set; }

    public sbyte Level { get; set; }

    public int Expirience { get; set; }

    public int RecoverableExp { get; set; }

    public int Hp { get; set; }

    public int Mp { get; set; }

    public int LaborPower { get; set; }

    public DateTime LaborPowerModified { get; set; }

    public int ConsumedLp { get; set; }

    public sbyte Ability1 { get; set; }

    public sbyte Ability2 { get; set; }

    public sbyte Ability3 { get; set; }

    public uint WorldId { get; set; }

    public uint ZoneId { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public float Roll { get; set; }

    public uint FactionId { get; set; }

    public string FactionName { get; set; }

    public int ExpeditionId { get; set; }

    public uint Family { get; set; }

    public uint DeadCount { get; set; }

    public DateTime DeadTime { get; set; }

    public int RezWaitDuration { get; set; }

    public DateTime RezTime { get; set; }

    public int RezPenaltyDuration { get; set; }

    public DateTime LeaveTime { get; set; }

    public long Money { get; set; }

    public long Money2 { get; set; }

    public int HonorPoint { get; set; }

    public int VocationPoint { get; set; }

    public int CrimePoint { get; set; }

    public int CrimeRecord { get; set; }

    public int HostileFactionKills { get; set; }

    public int PvpHonor { get; set; }

    public DateTime DeleteRequestTime { get; set; }

    public DateTime TransferRequestTime { get; set; }

    public DateTime DeleteTime { get; set; }

    public int BmPoint { get; set; }

    public bool AutoUseAapoint { get; set; }

    public int PrevPoint { get; set; }

    public int Point { get; set; }

    public int Gift { get; set; }

    public byte NumInvSlot { get; set; }

    public ushort NumBankSlot { get; set; }

    public sbyte ExpandedExpert { get; set; }

    public byte[] Slots { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int Deleted { get; set; }

    public int ReturnDistrict { get; set; }
}
