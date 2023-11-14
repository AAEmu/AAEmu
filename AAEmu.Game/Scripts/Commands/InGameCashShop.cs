using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands
{
    public class InGameCashShop : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "ingamecashshop", "ics" };
            CommandManager.Instance.Register(name, this);
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
                doCommand = args[0].ToLower();

            switch (doCommand)
            {
                case "list":
                    var allItems = CashShopManager.Instance.GetCashShopItems();
                    character.SendMessage(ChatType.System, $"[InGameCashShop] Currently loaded {allItems.Count} entries");
                    break;
                case "on":
                    CashShopManager.Instance.EnabledShop();
                    character.SendMessage(ChatType.System, $"[InGameCashShop] Shop has been enabled");
                    break;
                case "off":
                    CashShopManager.Instance.DisableShop();
                    character.SendMessage(ChatType.System, $"[InGameCashShop] Shop has been disabled");
                    break;
                case "reload":
                    if (CashShopManager.Instance.Enabled)
                    {
                        character.SendMessage(ChatType.System, $"[InGameCashShop] First disable the shop before reloading it");
                    }
                    else
                    {
                        CashShopManager.Instance.Load();
                        character.SendMessage(ChatType.System, $"[InGameCashShop] Items reloaded");
                    }
                    break;
                default:
                    character.SendMessage(ChatType.System, $"[InGameCashShop] Unknown command: {doCommand}");
                    break;
            }
        }
    }
}
