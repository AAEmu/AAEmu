using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game
{
    public class DominionData : PacketMarshaler
    {
        public ushort ZoneId { get; set; }
        public uint ExpeditionId { get; set; }
        public uint House { get; set; } // TODO id?
        public int TaxRate { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int CurHouseTaxMoney { get; set; }
        public int CurHuntTaxMoney { get; set; }
        public int PeaceTaxMoney { get; set; }
        public int CurHouseTaxAaPoint { get; set; }
        public int PeaceTaxAaPoint { get; set; }
        public DateTime LastPaidTime { get; set; }
        public DateTime LastSiegeEndTime { get; set; }
        public DateTime ReignStartTime { get; set; }
        public DateTime LastTaxRateChangedTime { get; set; } // TODO in struct long
        public DateTime LastNationalTaxRateChagedTime { get; set; } // TODO in struct long
        public ushort NationalTaxRate { get; set; }
        public long NationalMonumentDbId { get; set; }
        public float NationalMonumentX { get; set; }
        public float NationalMonumentY { get; set; }
        public float NationalMonumentZ { get; set; }
        public uint ObjId { get; set; }
        public DominionTerritoryData TerritoryData { get; set; }
        public DominionSiegeTimers SiegeTimers { get; set; } // TODO mb not correct namings
        public DateTime NonPvPStart { get; set; }
        public ushort NonPvPDuration { get; set; }
        
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(ZoneId);
            stream.Write(ExpeditionId);
            stream.Write(House);
            stream.Write(TaxRate);
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongY(Y));
            stream.Write(Z);
            stream.Write(CurHouseTaxMoney);
            stream.Write(CurHuntTaxMoney);
            stream.Write(PeaceTaxMoney);
            stream.Write(CurHouseTaxAaPoint);
            stream.Write(PeaceTaxAaPoint);
            stream.Write(LastPaidTime);
            stream.Write(LastSiegeEndTime);
            stream.Write(ReignStartTime);
            stream.Write(LastTaxRateChangedTime);
            stream.Write(LastNationalTaxRateChagedTime);
            stream.Write(NationalTaxRate);
            stream.Write(NationalMonumentDbId);
            stream.Write(Helpers.ConvertLongX(NationalMonumentX));
            stream.Write(Helpers.ConvertLongY(NationalMonumentY));
            stream.Write(NationalMonumentZ);
            stream.WriteBc(ObjId);
            stream.Write(TerritoryData);
            stream.Write(SiegeTimers);
            stream.Write(NonPvPStart);
            stream.Write(NonPvPDuration);
            return stream;
        }
    }

    public class DominionTerritoryData : PacketMarshaler
    {
        public uint Id { get; set; }
        public uint Id2 { get; set; }
        public byte MaxGates { get; set; }
        public byte MaxWalls { get; set; }
        public short RadiusDeclare { get; set; }
        public ushort RadiusDominion { get; set; }
        public short RadiusOffenseHq { get; set; }
        public short RadiusSiege { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Id2);
            stream.Write(MaxGates);
            stream.Write(MaxWalls);
            stream.Write(RadiusDeclare);
            stream.Write(RadiusDominion);
            stream.Write(RadiusOffenseHq);
            stream.Write(RadiusSiege);
            return stream;
        }
    }

    public class DominionSiegeTimers : PacketMarshaler
    {
        public int[] Durations { get; set; } = new int[5];
        public DateTime Started { get; set; }
        public DateTime Fixed { get; set; }
        public int Bdm { get; set; }
        
        public byte SiegePeriod { get; set; }
        
        public DominionUnkData UnkData { get; set; }
        public DominionUnkData Unk2Data { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            foreach (var duration in Durations)
                stream.Write(duration);
            stream.Write(Started);
            stream.Write(Fixed);
            stream.Write(Bdm);
            // ---------------------------------
            stream.Write(SiegePeriod);
            // ---------------------------------
            stream.Write(UnkData);
            stream.Write(Unk2Data);
            return stream;
        }
    }

    public class DominionUnkData : PacketMarshaler
    {
        public uint Id { get; set; } // TODO ExpeditionId
        public uint ObjId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public byte Ni { get; set; }
        public byte Nr { get; set; }
        
        public byte Limit { get; set; }
        public uint[] UnkIds { get; set; }
        
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.WriteBc(ObjId);
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongY(Y));
            stream.Write(Z);
            stream.Write(Ni);
            stream.Write(Nr);
            // -------------------------------
            stream.Write(Limit);
            stream.Write((byte)UnkIds.Length);
            foreach (var unkId in UnkIds)
                stream.Write(unkId);
            return stream;
        }
    }
}
