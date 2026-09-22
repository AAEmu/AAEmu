using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Craft order board test surface: read what the board holds, post and cancel through the real paths,
/// and push a row with values chosen by hand so a live client read can name the wire's fields.
/// </summary>
public class CraftOrderCommand : ICommand
{
    public string[] CommandNames { get; set; } = ["craftorder"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<list|add|cancel|sheet|probe|clear|purge> [itemId] [fee] [count] [grade]";
    }

    public string GetCommandHelpText()
    {
        return "craftorder list - every live order on the board.\n" +
               "craftorder add <itemId> [fee] - post an order through the normal path (escrow and rules apply).\n" +
               "craftorder cancel <orderId> - cancel one of your own orders and take the fee back.\n" +
               "craftorder sheet <craftId> [count] - consume a craft's materials and make its request sheet.\n" +
               "craftorder probe [itemId] [fee] [count] [grade] - push one row with these exact values.\n" +
               "craftorder clear - refund every live order (fee + sheet) and drop the board.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "list":
                List(character, messageOutput);
                break;
            case "add":
                Add(character, args, messageOutput);
                break;
            case "cancel":
                Cancel(character, args, messageOutput);
                break;
            case "sheet":
                Sheet(character, args, messageOutput);
                break;
            case "purge":
                Purge(character, args, messageOutput);
                break;
            case "probe":
                Probe(character, args, messageOutput);
                break;
            case "clear":
                messageOutput.SendMessage(CraftOrderManager.Instance.Clear()
                    ? $"[{CommandNames[0]}] board cleared"
                    : $"[{CommandNames[0]}] clear refused: the store could not be wiped, the board is unchanged");
                break;
            default:
                messageOutput.SendMessage($"[{CommandNames[0]}] {GetCommandLineHelp()}");
                break;
        }
    }

    private void List(Character character, IMessageOutput messageOutput)
    {
        var orders = CraftOrderManager.Instance.Orders.OrderBy(order => order.Id).ToList();
        messageOutput.SendMessage($"[{CommandNames[0]}] {orders.Count} live order(s)");
        foreach (var order in orders)
            messageOutput.SendMessage(
                $"  id={order.Id} owner={order.OwnerName}({order.OwnerId}) craft={order.CraftId} " +
                $"item={order.ItemId} count={order.Count} grade={order.Grade} fee={order.Fee} " +
                $"group={order.ActabilityGroupId} act={order.ActabilityPoint} expires={order.ExpiresUnix}");
        CraftOrderManager.Instance.SendOwnEntries(character);
        messageOutput.SendMessage($"[{CommandNames[0]}] pushed own entries to {character.Name}");
    }

    private void Add(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !ulong.TryParse(args[1], out var itemId))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] {GetCommandLineHelp()}");
            return;
        }

        var fee = args.Length > 2 && ulong.TryParse(args[2], out var parsedFee) ? parsedFee : 0;
        CraftOrderManager.Instance.Post(character, itemId, fee);
        messageOutput.SendMessage($"[{CommandNames[0]}] posted item {itemId} for {fee} copper");
    }

    private void Cancel(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !ulong.TryParse(args[1], out var orderId))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] {GetCommandLineHelp()}");
            return;
        }

        CraftOrderManager.Instance.Cancel(character, orderId);
        messageOutput.SendMessage($"[{CommandNames[0]}] cancel sent for order {orderId}");
    }

    /// <summary>
    /// Makes a request sheet the way the folio's cast does — same checks, same consumption — so the
    /// flow can be driven without sitting through the four second cast.
    /// </summary>
    private void Sheet(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var craftId))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] {GetCommandLineHelp()}");
            return;
        }

        var count = args.Length > 2 && uint.TryParse(args[2], out var parsedCount) ? parsedCount : 1u;
        if (CraftOrderManager.Instance.TryCraftSheet(character, craftId, count, out var reason))
            messageOutput.SendMessage(
                $"[{CommandNames[0]}] made sheet item {CraftOrderContent.SheetItemId} for craft {craftId} x{count}");
        else
            messageOutput.SendMessage($"[{CommandNames[0]}] sheet refused: {reason}");
    }

    /// <summary>Test cleanup: takes every stack of one item back out of the bag.</summary>
    private void Purge(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var templateId))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] {GetCommandLineHelp()}");
            return;
        }

        var stacks = character.Inventory.Bag.Items.Where(item => item.TemplateId == templateId).ToList();
        foreach (var stack in stacks)
            character.Inventory.Bag.ConsumeItem(ItemTaskType.Gm, templateId, stack.Count, null);

        messageOutput.SendMessage($"[{CommandNames[0]}] removed {stacks.Count} stack(s) of item {templateId}");
    }

    /// <summary>
    /// Pushes one row whose values are chosen by the caller, so the client's own read of the board
    /// can be matched against them field by field.
    /// </summary>
    private void Probe(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var itemId))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] probe <itemId> <fee> <count> <grade>");
            return;
        }

        if (args.Length < 5 ||
            !ulong.TryParse(args[2], out var fee) ||
            !uint.TryParse(args[3], out var count) ||
            !byte.TryParse(args[4], out var grade))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] probe <itemId> <fee> <count> <grade>");
            return;
        }

        var craftId = CraftManager.Instance.TryFindOrderableCraftByProduct(itemId, out var craft) ? craft.Id : 0u;
        var now = DateTimeOffset.UtcNow;

        var entry = new CraftOrderEntry(
            Id: 0x0102030405060708ul,
            Kind: 2,
            Unnamed1: CraftOrderProcessRules.InstantOwnerId(character.Id),
            OrderItemId: itemId,
            Unnamed2: craftId,
            CraftCount: count,
            Unnamed3: grade,
            MoneyAmount: fee,
            Unnamed4: 4,
            ActabilityPoint: 12_345,
            PostDate: (ulong)now.ToUnixTimeSeconds(),
            ExpireDate: (ulong)now.Add(CraftOrderManager.ListingLifetime).ToUnixTimeSeconds(),
            Status: 1,
            Unnamed5: 0x1112131415161718ul);

        character.SendPacket(new SCInsertCraftOrderEntryPacket(entry));
        character.SendPacket(new SCLoadCraftOrderEntryPacket([entry]));

        messageOutput.SendMessage(
            $"[{CommandNames[0]}] probe id=0102030405060708 kind=2 craft={craftId} item={itemId} " +
            $"count={count} grade={grade} fee={fee} group=4 act=12345 status=1 tail=1112131415161718");
    }
}
