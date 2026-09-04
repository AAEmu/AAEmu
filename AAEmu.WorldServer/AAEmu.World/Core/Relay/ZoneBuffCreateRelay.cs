namespace AAEmu.World.Core.Relay;

/// <summary>
/// How a WZBuffCreated body is applied when the zone may already hold that instance.
/// </summary>
public enum ZoneBuffCreateAction
{
    /// <summary>First Create for this (unit, index) on this zone instance.</summary>
    Create,

    /// <summary>
    /// The instance already exists and the incoming stack differs. Remove then Create so the zone
    /// refolds attributes from one entry at the new count. A second Create without Remove would
    /// register another entry and multiply the effect. An Update writes the count but leaves the
    /// attributes computed on the original Create in place.
    /// </summary>
    Replace,

    /// <summary>
    /// Same stack as last accepted (or the body has no readable stack). Keep the existing entry;
    /// charge and duration go out on Update.
    /// </summary>
    Skip
}

/// <summary>
/// Decides Create / Replace / Skip for a WZBuffCreated rebuild. No I/O — the caller sends the packets.
/// </summary>
public static class ZoneBuffCreateRelay
{
    public static ZoneBuffCreateAction Decide(uint? recordedStack, uint? incomingStack)
    {
        if (recordedStack is null)
            return ZoneBuffCreateAction.Create;

        if (incomingStack is { } incoming && incoming != recordedStack)
            return ZoneBuffCreateAction.Replace;

        return ZoneBuffCreateAction.Skip;
    }
}
