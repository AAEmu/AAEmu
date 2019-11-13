using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.DB.Game;

namespace AAEmu.Game.Models.Game
{
    public class Portal : PacketMarshaler
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint ZoneId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float ZRot { get; set; }

        public uint SubZoneId { get; set; }
        public uint Owner { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Name); // TODO max length 128
            stream.Write(ZoneId);
            stream.Write(X);
            stream.Write(Y);
            stream.Write(Z);
            stream.Write(ZRot);
            return stream;
        }

        public PortalBookCoords ToEntity()
            =>
            new PortalBookCoords()
            {
                Id        =       Id        ,
                Name      =       Name      ,
                X         = (int) X         ,
                Y         = (int) Y         ,
                Z         = (int) Z         ,
                ZoneId    =       ZoneId    ,
                ZRot      = (int) ZRot      ,
                Owner     =       Owner     ,
                SubZoneId =       SubZoneId ,
            };

        public static explicit operator Portal(PortalBookCoords v)
            =>
            new Portal
            {
                Id        =        v.Id         ,
                Name      =        v.Name       ,
                X         = (uint) v.X          ,
                Y         = (uint) v.Y          ,
                Z         = (uint) v.Z          ,
                ZoneId    =        v.ZoneId     ,
                ZRot      = (uint) v.ZRot       ,
                SubZoneId =        v.SubZoneId  ,
                Owner     =        v.Owner      ,
            };

    }

    public class VisitedDistrict
    {
        public uint Id { get; set; }
        public uint SubZone { get; set; }
        public uint Owner { get; set; }

        public PortalVisitedDistrict ToEntity()
            =>
            new PortalVisitedDistrict()
            {
                Id      = this.Id      ,
                Subzone = this.SubZone ,
                Owner   = this.Owner   ,
            };


        public static explicit operator VisitedDistrict(PortalVisitedDistrict v)
            =>
            new VisitedDistrict
            {
                Id      = v.Id       ,
                SubZone = v.Subzone  ,
                Owner   = v.Owner    ,
            };
    }
}
