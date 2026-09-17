using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM diagnostic for the rebuild content a house is offered: which packs exist, what each pack lets a house
/// become, and what one target costs. The client's own rebuild window draws the same tables locally, so this
/// is how the server's copy can be read back and compared without owning the house.
/// </summary>
public class HouseRebuild : ICommand
{
    public string[] CommandNames { get; set; } = ["houserebuild"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[rebuildingId]";
    }

    public string GetCommandHelpText()
    {
        return "With no argument, lists the housing rebuild packs and the targets each pack offers. " +
               "With a rebuilding id, prints that target: the housing it becomes, the skill that starts it, " +
               "the labor it spends and every material it costs.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            ListPacks(messageOutput);
            return;
        }

        if (!uint.TryParse(args[0], out var rebuildingId))
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /houserebuild [rebuildingId]");
            return;
        }

        var target = HousingGameData.Instance.GetRebuildTarget(rebuildingId);
        if (target == null)
        {
            CommandManager.SendNormalText(this, messageOutput, $"No rebuild target {rebuildingId}.");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"target {target.Id}: {target.Name} -> housing {target.HousingId}, skill {target.SkillId}, " +
            $"labor {target.LaborPower}, {target.Materials.Count} material(s)");
        foreach (var material in target.Materials)
            CommandManager.SendNormalText(this, messageOutput, $"  material item {material.ItemId} x{material.Count}");
    }

    private void ListPacks(IMessageOutput messageOutput)
    {
        var packs = HousingGameData.Instance.GetRebuildPacks().ToList();
        if (packs.Count == 0)
        {
            CommandManager.SendNormalText(this, messageOutput,
                "No rebuild packs loaded - the housing rebuild tables are empty.");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput, $"{packs.Count} rebuild pack(s):");
        foreach (var pack in packs)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"  pack {pack.Id}: {pack.Name}, {pack.TargetIds.Count} target(s)");
            foreach (var targetId in pack.TargetIds)
            {
                var target = HousingGameData.Instance.GetRebuildTarget(targetId);
                if (target == null)
                {
                    CommandManager.SendNormalText(this, messageOutput, $"    target {targetId}: (not loaded)");
                    continue;
                }

                CommandManager.SendNormalText(this, messageOutput,
                    $"    target {target.Id}: {target.Name} -> housing {target.HousingId}, skill {target.SkillId}, " +
                    $"labor {target.LaborPower}, {target.Materials.Count} material(s)");
            }
        }
    }
}
