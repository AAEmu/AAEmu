using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Player mounts and pets
/// </summary>
public partial class Mate
{
    public uint Id { get; set; }

    public ulong ItemId { get; set; }

    public string Name { get; set; }

    public int Xp { get; set; }

    public sbyte Level { get; set; }

    public int Mileage { get; set; }

    public int Hp { get; set; }

    public int Mp { get; set; }

    public uint Owner { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
