using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// The roots that hold loose zone level files (<c>worlds/&lt;world&gt;/level_design/...</c>).
/// </summary>
/// <remarks>
/// The <c>AAEMU_ZONE_GAME_DATA_ROOT</c> environment override wins; the configured root is the
/// backstop. Missing configuration yields an empty set, so a reader skips its level files instead of
/// falling back to a machine-specific disk. One implementation, so a new level-file reader cannot
/// drift from the existing ones.
/// </remarks>
public static class ZoneGameDataRoots
{
    public const string EnvVarName = "AAEMU_ZONE_GAME_DATA_ROOT";

    public static List<string> Enumerate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Offer(Environment.GetEnvironmentVariable(EnvVarName));
        Offer(AppConfiguration.Instance.ZoneGameDataRoot);
        return [.. seen];

        void Offer(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            try
            {
                var full = Path.GetFullPath(candidate.Trim());
                if (Directory.Exists(full))
                    seen.Add(full);
            }
            catch (Exception)
            {
                // A bad candidate is skipped so the other one can still apply.
            }
        }
    }
}
