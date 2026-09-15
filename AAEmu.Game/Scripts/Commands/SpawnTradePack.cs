using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class SpawnTradePack : ICommand
{
    private const uint DefaultTemplateId = 31832;

    public string[] CommandNames { get; set; } = ["spawntradepack", "tradepack"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[templateId=31832] [productionZoneGroupId=authored]";
    }

    public string GetCommandHelpText()
    {
        return "Spawns and equips a fresh trade pack using its authored production zone. " +
               "An explicit production zone group may be supplied for templates without an authored zone.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 2 ||
            args.Length > 0 && !uint.TryParse(args[0], out _) ||
            args.Length > 1 && !uint.TryParse(args[1], out _))
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var templateId = args.Length > 0 ? uint.Parse(args[0]) : DefaultTemplateId;
        var template = ItemManager.Instance.GetTemplate(templateId);
        if (template is not BackpackTemplate backpackTemplate ||
            !SpecialtyPackMaterializer.RequiresProductionContext(template))
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Item template {templateId} is not a freshness-enabled trade pack.");
            return;
        }

        var currentZoneGroupId = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId)?.GroupId ?? 0;
        uint? requestedZoneGroupId = args.Length > 1 ? uint.Parse(args[1]) : null;
        var productionZoneGroupId = ResolveProductionZoneGroupId(backpackTemplate, currentZoneGroupId, requestedZoneGroupId);
        if (productionZoneGroupId == 0 ||
            productionZoneGroupId > ushort.MaxValue ||
            ZoneManager.Instance.GetZoneGroupById(productionZoneGroupId) == null)
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Production zone group {productionZoneGroupId} is invalid or not loaded.");
            return;
        }

        var freshnessStartTime = DateTime.UtcNow;
        var productionContext = new SpecialtyPackProductionContext(
            SpecialtyPackProductionSource.Craft,
            freshnessStartTime,
            productionZoneGroupId,
            character.Id);
        if (!SpecialtyPackMaterializer.CanMaterialize(template, productionContext))
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Production zone group {productionZoneGroupId} does not match item {templateId}'s authored zone {template.SpecialtyZoneId}.");
            return;
        }

        if (!character.Inventory.TryAddNewItem(
                ItemTaskType.Gm,
                templateId,
                1,
                crafterId: character.Id,
                specialtyProductionContext: productionContext))
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Trade pack could not be created or equipped. Ensure the backpack can be moved to the inventory.");
            return;
        }

        var grade = (byte)Math.Clamp(template.FixedGrade, byte.MinValue, byte.MaxValue);
        CommandManager.SendNormalText(this, messageOutput,
            $"Spawned {ChatConverter.ConvertAsChatMessageReference(templateId, grade)} " +
            $"with freshness {freshnessStartTime:O} from zone group {productionZoneGroupId}.");
    }

    internal static uint ResolveProductionZoneGroupId(
        BackpackTemplate template,
        uint currentZoneGroupId,
        uint? requestedZoneGroupId)
    {
        return requestedZoneGroupId ??
               (template.SpecialtyZoneId != 0 ? template.SpecialtyZoneId : currentZoneGroupId);
    }
}
