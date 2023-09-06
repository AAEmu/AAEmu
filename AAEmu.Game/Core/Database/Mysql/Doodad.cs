using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Persistent doodads (e.g. tradepacks, furniture)
/// </summary>
public partial class Doodad
{
    public uint Id { get; set; }

    /// <summary>
    /// Character DB Id
    /// </summary>
    public int? OwnerId { get; set; }

    public byte? OwnerType { get; set; }

    public int TemplateId { get; set; }

    public int CurrentPhaseId { get; set; }

    public DateTime PlantTime { get; set; }

    public DateTime GrowthTime { get; set; }

    public DateTime PhaseTime { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float Roll { get; set; }

    public float Pitch { get; set; }

    public float Yaw { get; set; }

    /// <summary>
    /// Item DB Id of the associated item
    /// </summary>
    public ulong ItemId { get; set; }

    /// <summary>
    /// House DB Id if it is on actual house land
    /// </summary>
    public uint HouseId { get; set; }

    /// <summary>
    /// doodads DB Id this object is standing on
    /// </summary>
    public uint ParentDoodad { get; set; }

    /// <summary>
    /// ItemTemplateId of associated item
    /// </summary>
    public uint ItemTemplateId { get; set; }

    /// <summary>
    /// ItemContainer Id for Coffers
    /// </summary>
    public uint ItemContainerId { get; set; }

    /// <summary>
    /// Doodad specific permissions if used
    /// </summary>
    public int Data { get; set; }
}
