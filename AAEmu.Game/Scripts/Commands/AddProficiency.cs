using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command for setting Actability ("proficiency" / life-skill) points and step directly.
/// AAEmu's Actability system tracks per-character points and step for each actability_id
/// (weapon specialties, crafting trees, etc). Vanilla retail gates Master via expert slots
/// which is out of scope here — this command bypasses normal limits for testing.
/// </summary>
public class AddProficiency : ICommand
{
    public string[] CommandNames { get; set; } = ["profic", "proficiency", "addprofic"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<action> <args>";
    }

    public string GetCommandHelpText()
    {
        return "Actions:\n" +
               "  list                                  Show all actabilities for target with points + step\n" +
               "  set [target] <id> <points> [step]     Set a single actability to given points (and optional step)\n" +
               "  all [target] <points> [step]          Set EVERY actability to given points (and optional step)\n" +
               "  max [target]                          Set every actability to max points + max step\n" +
               "Tip: actability IDs come from the actability_categories table.\n" +
               "Examples:\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " list\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " set 31 90000 5    (목재 / wood to 90000 pts, step 5)\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " all 90000 5\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " max";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                {
                    var target = args.Length >= 2
                        ? WorldManager.Instance.GetTargetOrSelf(character, args[1], out _)
                        : character;
                    ListActabilities(this, messageOutput, target);
                    return;
                }

            case "set":
                {
                    var target = WorldManager.Instance.GetTargetOrSelf(character, args[1], out var firstArg);
                    if (args.Length < firstArg + 3)
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: profic set [target] <id> <points> [step]");
                        return;
                    }
                    if (!uint.TryParse(args[firstArg + 1], out var id) || !int.TryParse(args[firstArg + 2], out var points))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "id must be uint, points must be int");
                        return;
                    }
                    byte? step = null;
                    if (args.Length > firstArg + 3 && byte.TryParse(args[firstArg + 3], out var s))
                        step = s;
                    SetOne(this, messageOutput, target, id, points, step);
                    return;
                }

            case "all":
                {
                    var target = WorldManager.Instance.GetTargetOrSelf(character, args[1], out var firstArg);
                    if (args.Length < firstArg + 2)
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: profic all [target] <points> [step]");
                        return;
                    }
                    if (!int.TryParse(args[firstArg + 1], out var points))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "points must be int");
                        return;
                    }
                    byte? step = null;
                    if (args.Length > firstArg + 2 && byte.TryParse(args[firstArg + 2], out var s))
                        step = s;
                    SetAll(this, messageOutput, target, points, step);
                    return;
                }

            case "max":
                {
                    var target = args.Length >= 2
                        ? WorldManager.Instance.GetTargetOrSelf(character, args[1], out _)
                        : character;
                    // Step 7 (highest rank in the s_expMultipliers array). Points = 90000 (rank 7 entry).
                    SetAll(this, messageOutput, target, 90000, 7);
                    return;
                }

            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown action: {args[0]}");
                return;
        }
    }

    private static void ListActabilities(ICommand cmd, IMessageOutput out_, Character target)
    {
        if (target?.Actability?.Actabilities == null || target.Actability.Actabilities.Count == 0)
        {
            CommandManager.SendNormalText(cmd, out_, $"{target?.Name ?? "?"}: no actabilities loaded.");
            return;
        }
        CommandManager.SendNormalText(cmd, out_,
            $"Actabilities for {target.Name} ({target.Actability.Actabilities.Count} entries):");
        foreach (var (id, act) in target.Actability.Actabilities)
        {
            CommandManager.SendNormalText(cmd, out_,
                $"  id={id} step={act.Step} points={act.Point}");
        }
    }

    private static void SetOne(ICommand cmd, IMessageOutput out_, Character target, uint id, int points, byte? step)
    {
        if (target?.Actability?.Actabilities == null)
        {
            CommandManager.SendErrorText(cmd, out_, "Target has no actability container loaded.");
            return;
        }
        EnsureActabilityExists(target, id);
        if (!target.Actability.Actabilities.TryGetValue(id, out var actability))
        {
            CommandManager.SendErrorText(cmd, out_, $"Unknown actability id={id} (not in CharacterManager templates).");
            return;
        }
        var newStep = step ?? actability.Step;
        actability.Point = points;
        actability.Step = newStep;
        // SCActabilityPacket carries Id+Point+Step for every entry — the proper UI refresh.
        // SCExpertLimitModifiedPacket only changes step, NOT points, so it leaves "0" on screen.
        target.Actability.Send();
        CommandManager.SendNormalText(cmd, out_,
            $"Set actability id={id} on {target.Name} to step={newStep}, points={points}");
    }

    private static void SetAll(ICommand cmd, IMessageOutput out_, Character target, int points, byte? step)
    {
        if (target?.Actability?.Actabilities == null)
        {
            CommandManager.SendErrorText(cmd, out_, "Target has no actability container loaded.");
            return;
        }
        EnsureAllActabilitiesExist(target);
        var n = 0;
        foreach (var (id, actability) in target.Actability.Actabilities)
        {
            actability.Point = points;
            if (step.HasValue)
                actability.Step = step.Value;
            n++;
        }
        // ONE refresh packet at the end carries every Id+Point+Step — much cheaper than per-id.
        target.Actability.Send();
        CommandManager.SendNormalText(cmd, out_,
            $"Set {n} actabilities on {target.Name} to points={points}{(step.HasValue ? $", step={step}" : "")}");
    }

    /// <summary>
    /// If the target's Actabilities dict is empty (legacy character pre-dating the Create-loop, or
    /// fresh load with no saved rows), populate it from CharacterManager's templates so a /profic
    /// set/all/max actually has something to mutate.
    /// </summary>
    private static void EnsureAllActabilitiesExist(Character target)
    {
        if (target.Actability.Actabilities.Count > 0)
            return;
        // CharacterManager._actabilities is private; we can't enumerate it directly. Instead,
        // probe a reasonable id range. ActabilityCategories ids run from 1..273 in vanilla 1.2
        // and ActabilityTemplate ids overlap that range. Misses are silently skipped.
        for (uint i = 1; i <= 300; i++)
            EnsureActabilityExists(target, i);
    }

    private static void EnsureActabilityExists(Character target, uint id)
    {
        if (target.Actability.Actabilities.ContainsKey(id))
            return;
        // GetActability throws KeyNotFoundException on miss — wrap it to silently skip unknowns.
        AAEmu.Game.Models.Game.Char.Templates.ActabilityTemplate template;
        try { template = CharacterManager.Instance.GetActability(id); }
        catch { return; }
        if (template == null)
            return;
        target.Actability.Actabilities.Add(id, new Actability(template) { Id = id });
    }
}
