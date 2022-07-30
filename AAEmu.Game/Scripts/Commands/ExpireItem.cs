using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class ExpireItem : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "expireitem", "expire_item" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "<itemId> [minutes]";
        }

        public string GetCommandHelpText()
        {
            return "Sets item's expiration time to [minutes] minutes, 30 seconds if no time is provided. For debugging only.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[ExpireItem] No item Id provided");
                return;
            }

			ulong itemId = 0;
            if ( (args.Length > 0) && (ulong.TryParse(args[0], out var argitemId)) )
                itemId = argitemId;

            double minutes = 0.5;
            if ( (args.Length > 1) && (double.TryParse(args[1], out var argMinutes)) )
                minutes = argMinutes;

            if (itemId <= 0)
            {
                character.SendMessage("[ExpireItem] invalid itemId");
                return;
            }

			var item = ItemManager.Instance.GetItemByItemId(itemId);
			var newTime = DateTime.UtcNow.AddMinutes(minutes);
			if (minutes >= 0)
			{
				item.ExpirationTime = newTime ;
	            character.SendPacket(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, newTime));
			}
			else
			{
				item.ExpirationTime = DateTime.MinValue ;
	            character.SendPacket(new SCSyncItemLifespanPacket(false, item.Id, item.TemplateId, DateTime.MinValue));
			}
            character.SendMessage($"Item expire time updated to {newTime}");
        }
    }
}
