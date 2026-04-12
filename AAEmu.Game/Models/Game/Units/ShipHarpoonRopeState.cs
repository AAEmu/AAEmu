using System;
using System.Numerics;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>Active harpoon line for a ship harpoon cannon slave. Struct so each <see cref="Slave"/> embeds state without a per-slave heap object.</summary>
public struct ShipHarpoonRopeState
{
    public bool IsEngaged { get; set; }
    /// <summary>World hook when <see cref="HookBasisObjId"/> is 0; otherwise snapshot at engage (resolver uses basis + local).</summary>
    public Vector3 HookWorld { get; set; }
    /// <summary>When non-zero, hook point is in this unit's local basis (e.g. hull hit = cast <c>ObjId1</c> + local offset).</summary>
    public uint HookBasisObjId { get; set; }
    public Vector3 HookLocalInBasis { get; set; }
    public float RopeLength { get; set; }
    public bool LastTeared { get; set; }
    public bool LastCutout { get; set; }
    /// <summary>Max range from Launch Harpoon skill template (world units); used to auto-break when hook moves out.</summary>
    public float MaxLaunchRange { get; set; }
    /// <summary>Hook is a world point on land (not water); enables hull tow physics toward the hook.</summary>
    public bool HookAttachedToTerrain { get; set; }
    /// <summary>
    /// When non-null, server clears engaged rope at this time so tow physics does not outlive the client rope UI
    /// (compact <c>skill_controllers</c> Rope value1/value2 ms — we use the minimum positive of the first two values).
    /// </summary>
    public DateTime? ControllerExpireAtUtc { get; set; }

    public void Clear()
    {
        IsEngaged = false;
        HookWorld = default;
        HookBasisObjId = 0;
        HookLocalInBasis = default;
        RopeLength = 0f;
        LastTeared = false;
        LastCutout = false;
        MaxLaunchRange = 0f;
        HookAttachedToTerrain = false;
        ControllerExpireAtUtc = null;
    }
}
