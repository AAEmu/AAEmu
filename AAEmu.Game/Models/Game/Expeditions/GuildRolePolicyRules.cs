namespace AAEmu.Game.Models.Game.Expeditions;

/// <summary>
/// Guilds created or migrated without <c>expedition_role_policies</c> rows leave the
/// client RolePolicyList empty, which breaks the guild management UI. Creation already
/// seeds defaults from config; load must do the same when the table is empty.
/// </summary>
internal static class GuildRolePolicyRules
{
    public static bool NeedsDefaultPolicies(int policyCount) => policyCount <= 0;
}
