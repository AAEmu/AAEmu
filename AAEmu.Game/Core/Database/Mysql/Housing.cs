using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Player buildings
/// </summary>
public partial class Housing
{
    public int Id { get; set; }

    public uint AccountId { get; set; }

    public uint Owner { get; set; }

    public uint CoOwner { get; set; }

    public uint TemplateId { get; set; }

    public string Name { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public float Roll { get; set; }

    public sbyte CurrentStep { get; set; }

    public int CurrentAction { get; set; }

    public sbyte Permission { get; set; }

    public DateTime PlaceDate { get; set; }

    public DateTime ProtectedUntil { get; set; }

    public uint FactionId { get; set; }

    public uint SellTo { get; set; }

    public long SellPrice { get; set; }

    public byte AllowRecover { get; set; }
}
