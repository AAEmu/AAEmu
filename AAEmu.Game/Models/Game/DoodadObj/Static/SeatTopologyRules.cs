namespace AAEmu.Game.Models.Game.DoodadObj.Static;

public static class SeatTopologyRules
{
    /// <summary>
    /// Only the rider positions are passenger seats. Mount skills also name mast, sail, equipment and
    /// presentation attach points; those must never be exposed as passenger capacity.
    /// </summary>
    public static bool IsRiderSeat(AttachPointKind attachPoint) =>
        attachPoint is AttachPointKind.Driver
            or AttachPointKind.Passenger0
            or AttachPointKind.Passenger1
            or AttachPointKind.Passenger2
            or AttachPointKind.Passenger3
            or AttachPointKind.Passenger4
            or AttachPointKind.Passenger5
            or AttachPointKind.Passenger6;

    /// <summary>
    /// Attach points that can carry a character onto a slave. The telescope is a content-backed
    /// DoodadFuncAttachment point, not a passenger slot, but it uses the same BindSlave path.
    /// </summary>
    public static bool IsBoardableAttachPoint(AttachPointKind attachPoint) =>
        IsRiderSeat(attachPoint) || attachPoint == AttachPointKind.Telescope;

    public static bool TryNormalize(int rawAttachPoint, out AttachPointKind attachPoint)
    {
        if (rawAttachPoint < byte.MinValue || rawAttachPoint > byte.MaxValue)
        {
            attachPoint = AttachPointKind.None;
            return false;
        }

        attachPoint = (AttachPointKind)(byte)rawAttachPoint;
        return IsBoardableAttachPoint(attachPoint);
    }

    public static IReadOnlyList<AttachPointKind> Normalize(IEnumerable<AttachPointKind> attachPoints) =>
        (attachPoints ?? [])
            .Where(IsBoardableAttachPoint)
            .Distinct()
            .OrderBy(point => (byte)point)
            .ToArray();

    /// <summary>
    /// Takes a stable rider snapshot before invoking an unmount callback. A callback commonly
    /// removes the binding from the source dictionary, so enumerating the live dictionary directly
    /// would skip riders during death/despawn cleanup.
    /// </summary>
    public static IReadOnlyList<AttachPointKind> ReleaseAllRiders<T>(
        IEnumerable<KeyValuePair<AttachPointKind, T>> riders,
        Action<AttachPointKind, T> release)
    {
        if (riders == null || release == null)
            return Array.Empty<AttachPointKind>();

        var snapshot = riders
            .Where(binding => binding.Value is not null)
            .ToArray();
        foreach (var binding in snapshot)
            release(binding.Key, binding.Value);

        return snapshot.Select(binding => binding.Key).ToArray();
    }
}
