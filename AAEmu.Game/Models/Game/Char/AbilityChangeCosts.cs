using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Gold costs for NPC skillset swap (formula 41) and skillsaver activate (formula 42).
/// Config bases come from compact <c>content_configs</c>: <c>change_ability</c> /
/// <c>swap_ability_set</c> (client maps these to <c>config_swap_ability_*</c>).
/// </summary>
internal static class AbilityChangeCosts
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const int DefaultChangeAbility = 600;
    private const int DefaultSwapAbilitySet = 600;
    private const int DefaultFreeActivations = 0;

    private static int? _changeAbility;
    private static int? _swapAbilitySet;
    private static int? _freeActivations;

    public static int FreeActivationLimit =>
        _freeActivations ??= LoadInt("ability_set_free_activation_count", DefaultFreeActivations);

    /// <summary>
    /// Charge inventory gold for an NPC skillset change. Returns false if the player cannot pay
    /// (<see cref="Character.ChangeMoney"/> already sends NotEnoughMoney).
    /// </summary>
    public static bool TryChargeSwapAbility(Character owner)
    {
        var cost = Evaluate(
            FormulaKind.SwapAbilityCost,
            owner,
            "config_swap_ability_cost",
            _changeAbility ??= LoadInt("change_ability", DefaultChangeAbility));
        if (cost <= 0)
            return true;

        return owner.ChangeMoney(SlotType.Inventory, -cost, ItemTaskType.AbilityChange);
    }

    /// <summary>
    /// Charge inventory gold for a skillsaver activation after free uses are exhausted.
    /// </summary>
    public static bool TryChargeSwapAbilitySet(Character owner)
    {
        var cost = Evaluate(
            FormulaKind.SwapAbilitySetCost,
            owner,
            "config_swap_ability_set_cost",
            _swapAbilitySet ??= LoadInt("swap_ability_set", DefaultSwapAbilitySet));
        if (cost <= 0)
            return true;

        return owner.ChangeMoney(SlotType.Inventory, -cost, ItemTaskType.AbilityChange);
    }

    private static long Evaluate(FormulaKind kind, Character owner, string configKey, int configValue)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)kind);
        if (formula == null)
        {
            Logger.Warn("Ability change cost: missing formula {0}", kind);
            return 0;
        }

        var value = formula.Evaluate(new Dictionary<string, double>
        {
            ["pc_level"] = owner.Level,
            ["heir_level"] = owner.HeirLevel,
            [configKey] = configValue
        });
        return (long)Math.Max(0, Math.Round(value));
    }

    private static int LoadInt(string name, int fallback)
    {
        try
        {
            using var connection = SQLite.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT c.value FROM content_configs c " +
                "JOIN enum_content_configs e ON e.id = c.id " +
                "WHERE e.name = @name LIMIT 1";
            command.Parameters.AddWithValue("@name", name);
            var result = command.ExecuteScalar();
            if (result != null && result != DBNull.Value)
                return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Ability change cost: content_config '{0}' missing, using {1}", name, fallback);
        }

        return fallback;
    }
}
