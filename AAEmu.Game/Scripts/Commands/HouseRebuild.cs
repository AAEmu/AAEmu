using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
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
        return "[rebuildingId | create <housingDesignId>]";
    }

    public string GetCommandHelpText()
    {
        return "With no argument, lists the housing rebuild packs and the targets each pack offers. " +
               "With a rebuilding id, prints that target: the housing it becomes, the skill that starts it, " +
               "the labor it spends and every material it costs. " +
               "With 'create' and a housing design id, places that design where you stand the way the " +
               "client does, spending the design item out of your bag.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 0 && args[0].Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            CreateHouse(character, args.AsSpan(1), messageOutput);
            return;
        }

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

    /// <summary>
    /// Places a house for the caller where they stand, through the same path the client's placement request
    /// reaches. It is how a rebuild can be looked at on a server that has no player houses: build one, then
    /// start a rebuild on it.
    /// </summary>
    private void CreateHouse(Character character, ReadOnlySpan<string> args, IMessageOutput messageOutput)
    {
        if (args.Length == 0 || !uint.TryParse(args[0], out var designId))
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /houserebuild create <housingDesignId>");
            return;
        }

        if (character.Connection == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "No connection to build with.");
            return;
        }

        // Placement spends the design item out of the bag before it creates anything, and it is the item the
        // client names when it asks to place: without it nothing is built, so hand it over or say it is missing
        // rather than report a house that was never placed.
        var designItemTemplateId = HousingGameData.Instance.GetItemIdByDesign(designId);
        if (designItemTemplateId == 0)
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Housing design {designId} has no design item to place it with.");
            return;
        }

        if (!character.Inventory.Bag.GetAllItemsByTemplate(designItemTemplateId, -1, out var designItems, out _))
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Housing design {designId} needs item {designItemTemplateId} in the bag to be placed.");
            return;
        }

        // The placement answers nothing back and has its own refusals (patron status, housing area, tax), so
        // the command reads the result off the houses the caller owns instead of assuming it worked.
        var housesBefore = CountOwnHousesOfDesign(designId, character.Id, HousingManager.Instance.GetAllHouses());

        var position = character.Transform.World.Position;
        HousingManager.Instance.Build(character.Connection, designId,
            position.X, position.Y, position.Z, character.Transform.World.Rotation.Z,
            designItems[0].Id, autoUseAaPoint: false);

        var housesAfter = CountOwnHousesOfDesign(designId, character.Id, HousingManager.Instance.GetAllHouses());
        if (housesAfter > housesBefore)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"Placed housing design {designId} at {position.X:F1}, {position.Y:F1}, {position.Z:F1}.");
            return;
        }

        CommandManager.SendErrorText(this, messageOutput,
            $"Housing design {designId} was not placed - the placement was refused, see the World log for why.");
    }

    /// <summary>
    /// How many houses of this design the character already owns. The placement path reports nothing back, so
    /// this is what tells a house it placed from one it refused: another player's house, or the caller's house
    /// of a different design, is not this placement's result.
    /// </summary>
    public static int CountOwnHousesOfDesign(uint designId, uint ownerId, IEnumerable<House> houses) =>
        houses.Count(house => house.TemplateId == designId && house.OwnerId == ownerId);

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
