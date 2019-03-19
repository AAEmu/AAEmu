using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddGold : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("add_gold", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Gold] /add_gold <amount>");
                return;
            }

            if (int.TryParse(args[0], out var amount))
            {
                character.Money += amount;
                character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.AutoLootDoodadItem, new List<ItemTask> { new MoneyChange(amount) }, new List<ulong>()));
            }
            else
                character.SendMessage("[Gold] Params wrong...");
        }
    }
}
