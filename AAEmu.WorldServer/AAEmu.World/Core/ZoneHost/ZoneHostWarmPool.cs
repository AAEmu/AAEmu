using AAEmu.World.Models;

namespace AAEmu.World.Core.ZoneHost;

/// <summary>
/// Config/env helpers for the warm ZoneHost pool. Size and idle rules live here so
/// call sites never hardcode pool counts or zone keys.
/// </summary>
public static class ZoneHostWarmPool
{
    public const string EnvEnabled = "AAEMU_ZONEHOST_WARM_ENABLED";
    /// <summary>Kill switch alias: <c>1</c> = warm pool on, <c>0</c> = stopped (no idle hosts).</summary>
    public const string EnvWarmInstances = "AAEMU_WARM_INSTANCES";
    public const string EnvDefaultSize = "AAEMU_ZONEHOST_WARM_DEFAULT_SIZE";
    public const string EnvIdleSeconds = "AAEMU_ZONEHOST_WARM_IDLE_SECONDS";

    /// <summary>
    /// Local json first; env overrides when set. Invalid DefaultSize after resolve fails in
    /// <see cref="ResolveSize"/>.
    /// </summary>
    public static void ApplyEnvironmentOverrides(
        ZoneHostWarmPoolConfig config,
        Func<string, string> getEnv = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        getEnv ??= Environment.GetEnvironmentVariable;

        // Prefer the short kill switch when set; else AAEMU_ZONEHOST_WARM_ENABLED.
        var warmInstances = getEnv(EnvWarmInstances);
        var enabled = !string.IsNullOrEmpty(warmInstances) ? warmInstances : getEnv(EnvEnabled);
        if (enabled == "1")
            config.Enabled = true;
        else if (enabled == "0")
            config.Enabled = false;

        var defaultSizeRaw = getEnv(EnvDefaultSize);
        if (int.TryParse(defaultSizeRaw, out var defaultSize) && defaultSize > 0)
            config.DefaultSize = defaultSize;

        var idleRaw = getEnv(EnvIdleSeconds);
        if (int.TryParse(idleRaw, out var idle) && idle >= 0)
            config.IdleUnloadSeconds = idle;
    }

    /// <summary>
    /// Zone <paramref name="zoneSize"/> when &gt; 0; otherwise <paramref name="defaultSize"/>.
    /// Throws when the effective size is not positive.
    /// </summary>
    public static int ResolveSize(int zoneSize, int defaultSize)
    {
        var size = zoneSize > 0 ? zoneSize : defaultSize;
        if (size <= 0)
            throw new InvalidOperationException(
                "ZoneHost.WarmPool size must be > 0 (set zone Size or DefaultSize).");
        return size;
    }

    public static bool IsIdleDue(DateTime lastTouchUtc, DateTime nowUtc, int idleUnloadSeconds)
    {
        if (idleUnloadSeconds <= 0)
            return false;
        return nowUtc - lastTouchUtc >= TimeSpan.FromSeconds(idleUnloadSeconds);
    }

    public static bool IsTemplateConfigured(ZoneHostWarmPoolConfig config, string worldTemplateName)
    {
        if (config is not { Enabled: true } || string.IsNullOrWhiteSpace(worldTemplateName))
            return false;
        return config.Zones.Any(z =>
            !string.IsNullOrWhiteSpace(z.WorldTemplateName) &&
            string.Equals(z.WorldTemplateName.Trim(), worldTemplateName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static ZoneHostWarmZoneConfig FindZone(ZoneHostWarmPoolConfig config, string worldTemplateName)
    {
        if (config?.Zones == null || string.IsNullOrWhiteSpace(worldTemplateName))
            return null;
        return config.Zones.FirstOrDefault(z =>
            !string.IsNullOrWhiteSpace(z.WorldTemplateName) &&
            string.Equals(z.WorldTemplateName.Trim(), worldTemplateName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
