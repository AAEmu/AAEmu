using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts.SubCommands;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.SubCommands.Items;

public class ItemExpireSubCommand : SubCommandBase
{
    public ItemExpireSubCommand()
    {
        Title = "[Item]";
        Description = "Set target to expire after a specific amount of minutes, or 30 seconds if ommited.";
        CallPrefix = $"{CommandManager.CommandPrefix}item expire";
        AddParameter(new NumericSubCommandParameter<ulong>("itemId", "item id", true));
        AddParameter(
            new NumericSubCommandParameter<float>("minutes", "minutes=0.5", false, -1f, 1000000f)
            {
                DefaultValue = 0.5f
            });
    }

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        //Character addTarget;
        var selfCharacter = (Character)character;

        ulong itemId = parameters["itemId"];
        var minutes = GetOptionalParameterValue<float>(parameters, "minutes", -1);

        var item = ItemManager.Instance.GetItemByItemId(itemId);

        if (item == null)
        {
            character.SendMessage($"No item could be found with Id: {itemId}");
            return;
        }

        var newTime = DateTime.UtcNow.AddMinutes(minutes);
        if (minutes >= 0)
        {
            item.ExpirationTime = newTime;
            character.SendPacket(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, newTime));
            character.SendMessage($"Item @ITEM_NAME({item.TemplateId})'s expire time updated to {newTime}");
        }
        else
        {
            item.ExpirationTime = DateTime.MinValue;
            character.SendPacket(new SCSyncItemLifespanPacket(false, item.Id, item.TemplateId, DateTime.MinValue));
            character.SendMessage($"Item @ITEM_NAME({item.TemplateId})'s expire set to invalid");
        }
    }
}
