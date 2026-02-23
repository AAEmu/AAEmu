using System;

namespace CgfConverter.Models.Materials;

[Flags]
public enum MaterialFlags
{
    Wire = 0x0001,
    TwoSided = 0x0002,
    Additive = 0x0004,
    DetailDecal = 0x0008,
    Lighting = 0x0010,
    NoShadow = 0x0020,
    AlwaysUsed = 0x0040,
    PureChild = 0x0080,
    MultiSubmtl = 0x0100,
    NoPhysicalize = 0x0200,
    NoDraw = 0x0400,
    NoPreview = 0x0800,
    NotInstanced = 0x1000,
    CollisionProxy = 0x2000,
    Scatter = 0x4000,
    RequireForwardRendering = 0x8000,
    NonRemovable = 0x10000,
    HideOnBreak = 0x20000,
    UiMaterial = 0x40000,
    ShaderGenMask64Bit = 0x80000,
    RaycastProxy = 0x100000,
    RequireNearestCubemap = 0x200000,
}