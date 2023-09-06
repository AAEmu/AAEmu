using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

public partial class ItemContainer
{
    public uint ContainerId { get; set; }

    /// <summary>
    /// Partial Container Class Name
    /// </summary>
    public string ContainerType { get; set; }

    /// <summary>
    /// Internal Container Type
    /// </summary>
    public string SlotType { get; set; }

    /// <summary>
    /// Maximum Container Size
    /// </summary>
    public int ContainerSize { get; set; }

    /// <summary>
    /// Owning Character Id
    /// </summary>
    public uint OwnerId { get; set; }
}
