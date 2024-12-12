using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class InGameCashShop : ICommand
{
    public string[] CommandNames { get; set; } = ["ingamecashshop", "ics"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<on||off||reload>";
    }

    public string GetCommandHelpText()
    {
        return "Enables or disables the InGameCashShop as well as allows to reloading of the data";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var doCommand = "list";
        if (args.Length > 0)
        {
            doCommand = args[0].ToLower();
        }

        switch (doCommand)
        {
            case "list":
                CommandManager.SendNormalText(this, messageOutput,
                    $"Currently loaded {CashShopManager.Instance.ShopItems.Count} shop items listed as {CashShopManager.Instance.MenuItems.Count} entries across all tabs in the cash shop.");
                break;
            case "on":
                CashShopManager.Instance.EnabledShop();
                CommandManager.SendNormalText(this, messageOutput, $"Shop has been enabled");
                break;
            case "off":
                CashShopManager.Instance.DisableShop();
                CommandManager.SendNormalText(this, messageOutput, $"Shop has been disabled");
                break;
            case "reload":
                if (CashShopManager.Instance.Enabled)
                {
                    CommandManager.SendNormalText(this, messageOutput, $"First disable the shop before reloading it");
                }
                else
                {
                    CashShopManager.Instance.Load();
                    CommandManager.SendNormalText(this, messageOutput, $"Items reloaded");
                }

                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown command: {doCommand}");
                break;
        }
    }
}
