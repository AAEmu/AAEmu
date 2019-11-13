using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Characters
    {
        public uint Id { get; set; }
        public uint AccountId { get; set; }
        public string Name { get; set; }
        public int AccessLevel { get; set; }
        public byte Race { get; set; }
        public byte Gender { get; set; }
        public byte[] UnitModelParams { get; set; }
        public byte Level { get; set; }
        public int Expirience { get; set; }
        public int RecoverableExp { get; set; }
        public int Hp { get; set; }
        public int Mp { get; set; }
        public short LaborPower { get; set; }
        public DateTime LaborPowerModified { get; set; }
        public int ConsumedLp { get; set; }
        public byte Ability1 { get; set; }
        public byte Ability2 { get; set; }
        public byte Ability3 { get; set; }
        public uint WorldId { get; set; }
        public uint ZoneId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public sbyte RotationX { get; set; }
        public sbyte RotationY { get; set; }
        public sbyte RotationZ { get; set; }
        public uint FactionId { get; set; }
        public string FactionName { get; set; }
        public uint ExpeditionId { get; set; }
        public uint Family { get; set; }
        public short DeadCount { get; set; }
        public DateTime DeadTime { get; set; }
        public int RezWaitDuration { get; set; }
        public DateTime RezTime { get; set; }
        public int RezPenaltyDuration { get; set; }
        public DateTime LeaveTime { get; set; }
        public long Money { get; set; }
        public long Money2 { get; set; }
        public int HonorPoint { get; set; }
        public int VocationPoint { get; set; }
        public short CrimePoint { get; set; }
        public int CrimeRecord { get; set; }
        public DateTime DeleteRequestTime { get; set; }
        public DateTime TransferRequestTime { get; set; }
        public DateTime DeleteTime { get; set; }
        public int BmPoint { get; set; }
        public byte AutoUseAapoint { get; set; }
        public int PrevPoint { get; set; }
        public int Point { get; set; }
        public int Gift { get; set; }
        public byte NumInvSlot { get; set; }
        public short NumBankSlot { get; set; }
        public byte ExpandedExpert { get; set; }
        public byte[] Slots { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int Deleted { get; set; }
    }
}
