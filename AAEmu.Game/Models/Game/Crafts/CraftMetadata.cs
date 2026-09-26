namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>Level of the shipped craft category hierarchy.</summary>
public enum CraftCategoryLevel : byte
{
    A,
    B,
    C,
    D
}

/// <summary>A shipped craft whose C category and D category point to different C branches.</summary>
public readonly record struct CraftCategoryMismatch(
    uint CraftId,
    uint CraftCCategoryId,
    uint CraftDCategoryId,
    uint DCategoryParentId);

/// <summary>One row in the typed craft category hierarchy.</summary>
public sealed class CraftCategory
{
    public uint Id { get; init; }
    public CraftCategoryLevel Level { get; init; }
    public string Name { get; init; } = string.Empty;
    public int UiOrder { get; init; }
    public uint? ParentId { get; init; }

    // The category tables have level-specific columns. They remain nullable so a consumer
    // cannot accidentally treat a C/D flag as an A visibility value.
    public bool? Visible { get; init; }
    public bool? UseOnlyDoodad { get; init; }
    public string? ButtonDecoKey { get; init; }
    public string? ChildFilePath { get; init; }
    public int? RepresentedChildCount { get; init; }
    public string? Description { get; init; }

    public List<uint> ChildIds { get; } = [];
}

/// <summary>A named craft line from the compact catalog.</summary>
public sealed class CraftLine
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<CraftLineComponent> Components { get; } = [];
}

/// <summary>One craft-to-line relation and its one-based line rank.</summary>
public sealed class CraftLineComponent
{
    public uint Id { get; init; }
    public uint CraftId { get; init; }
    public uint CraftLineId { get; init; }
    public uint Rank { get; init; }
}

/// <summary>A craft pack and the craft ids assigned to it.</summary>
public sealed class CraftPack
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public HashSet<uint> CraftIds { get; } = [];
}
