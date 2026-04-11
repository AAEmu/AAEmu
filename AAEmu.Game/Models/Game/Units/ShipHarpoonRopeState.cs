using System.Numerics;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>Active harpoon line for a ship harpoon cannon slave.</summary>
public sealed class ShipHarpoonRopeState
{
    public bool IsEngaged { get; set; }
    public Vector3 HookWorld { get; set; }
    public float RopeLength { get; set; }
    public bool LastTeared { get; set; }
    public bool LastCutout { get; set; }

    public void Clear()
    {
        IsEngaged = false;
        HookWorld = default;
        RopeLength = 0f;
        LastTeared = false;
        LastCutout = false;
    }
}
