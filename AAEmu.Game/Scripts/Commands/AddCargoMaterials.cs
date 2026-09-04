using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddCargoMaterials : ICommand
{
    public string[] CommandNames { get; set; } = ["addcargomaterials", "cargomaterials"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[countEach | firstCount secondCount thirdCount [zoneGroupId]]";
    }

    public string GetCommandHelpText()
    {
        return "Adds test stock to each authored cargo material bucket. With no amounts, adds one complete recipe batch.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var currentZoneGroupId = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId)?.GroupId ?? 0;
        if (!TryParseArguments(args, currentZoneGroupId, out var zoneGroupId, out var amounts))
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var zoneGroup = ZoneManager.Instance.GetZoneGroupById(zoneGroupId);
        var categoryId = zoneGroup == null
            ? null
            : SpecialtyManager.GetTradeGoodCategoryId(zoneGroup.FactionChatRegionId);
        if (categoryId == null)
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Zone group {zoneGroupId} is not loaded or has no cargo category.");
            return;
        }

        var result = SpecialtyManager.Instance.AddTradeGoodMaterials(zoneGroupId, categoryId.Value, amounts);
        if (!result.Success)
        {
            CommandManager.SendErrorText(this, messageOutput, result.Error);
            return;
        }

        var stocks = string.Join(", ", result.Materials.Select(x => $"tag {x.TagId}: {x.Stock}/{x.RequiredCount}"));
        CommandManager.SendNormalText(this, messageOutput,
            $"Cargo recipe {result.TradeGoodId}, zone {zoneGroupId}: {stocks}; produced {result.Produced}, cargo stock {result.CargoStock}.");
    }

    internal static bool TryParseArguments(
        string[] args,
        uint currentZoneGroupId,
        out uint zoneGroupId,
        out uint[] amounts)
    {
        zoneGroupId = currentZoneGroupId;
        amounts = null;
        if (args.Length is not (0 or 1 or 3 or 4) || args.Any(x => !uint.TryParse(x, out _)))
            return false;

        if (args.Length == 0)
            return zoneGroupId != 0;
        if (args.Length == 1)
            amounts = [uint.Parse(args[0])];
        else
        {
            amounts = [uint.Parse(args[0]), uint.Parse(args[1]), uint.Parse(args[2])];
            if (args.Length == 4)
                zoneGroupId = uint.Parse(args[3]);
        }

        return zoneGroupId != 0;
    }
}
