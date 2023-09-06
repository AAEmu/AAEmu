using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// User Created Content (crests)
/// </summary>
public partial class Ucc
{
    public int Id { get; set; }

    /// <summary>
    /// PlayerID
    /// </summary>
    public int UploaderId { get; set; }

    public sbyte Type { get; set; }

    /// <summary>
    /// Raw uploaded UCC data
    /// </summary>
    public byte[] Data { get; set; }

    /// <summary>
    /// Background pattern
    /// </summary>
    public uint Pattern1 { get; set; }

    /// <summary>
    /// Crest
    /// </summary>
    public uint Pattern2 { get; set; }

    public uint Color1R { get; set; }

    public uint Color1G { get; set; }

    public uint Color1B { get; set; }

    public uint Color2R { get; set; }

    public uint Color2G { get; set; }

    public uint Color2B { get; set; }

    public uint Color3R { get; set; }

    public uint Color3G { get; set; }

    public uint Color3B { get; set; }

    public DateTime Modified { get; set; }
}
