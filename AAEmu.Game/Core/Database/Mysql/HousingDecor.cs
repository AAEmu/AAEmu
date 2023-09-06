using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

public partial class HousingDecor
{
    public int Id { get; set; }

    public int? HouseId { get; set; }

    public int? DesignId { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float QuatX { get; set; }

    public float QuatY { get; set; }

    public float QuatZ { get; set; }

    public float QuatW { get; set; }

    public long? ItemId { get; set; }

    public int ItemTemplateId { get; set; }
}
