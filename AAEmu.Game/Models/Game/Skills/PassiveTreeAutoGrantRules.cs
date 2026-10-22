using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Whether skill-tree passives (<c>passive_buffs.req_points &gt; 0</c>) are handed out automatically
/// when the tree qualifies, or left for the player to click via <c>CSLearnBuff</c>.
/// </summary>
/// <remarks>
/// Env <c>AAEMU_PASSIVE_TREE_AUTO_GRANT</c>: unset / anything other than <c>0</c>/<c>false</c>/<c>off</c>
/// → auto (default). <c>0</c>/<c>false</c>/<c>off</c> → click-to-learn. Click mode still gates on
/// skills unlocked in that tree and does not charge the shared skill-point pool (tree rows ship
/// <c>skill_points=0</c>), so a player can skip a passive they dislike and enable it later even
/// after spending every skill point on actives.
/// </remarks>
public static class PassiveTreeAutoGrantRules
{
    public const string EnvironmentName = "AAEMU_PASSIVE_TREE_AUTO_GRANT";

    /// <summary>Test hook; null reads the process environment.</summary>
    internal static bool? OverrideEnabled { get; set; }

    /// <summary>True when tree passives are granted as soon as <c>req_points</c> is met.</summary>
    public static bool TreePassivesAutoGrant
    {
        get
        {
            if (OverrideEnabled.HasValue)
                return OverrideEnabled.Value;

            var raw = Environment.GetEnvironmentVariable(EnvironmentName);
            if (string.IsNullOrWhiteSpace(raw))
                return true;

            return raw is not ("0" or "false" or "False" or "FALSE" or "off" or "Off" or "OFF");
        }
    }

    /// <summary>
    /// Whether <paramref name="template"/> belongs on the server auto-grant list (before level /
    /// already-held filters). Always-on actives with no tree investment stay auto either mode.
    /// </summary>
    public static bool IsAutoGrantCandidate(PassiveBuffTemplate template)
    {
        if (template == null)
            return false;

        // Shipwreck-style actives (req_points 0): always server-granted. Their skill_points
        // column does not spend the combat pool (PassiveSkillPointSpendRules).
        if (template.Active && template.ReqPoints <= 0)
            return true;

        // Tree investment passives (and actives that also need N skills in the tree).
        if (template.ReqPoints > 0)
            return TreePassivesAutoGrant;

        return false;
    }
}
