using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddBuff : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "buff", "addbuff", "add_buff", "buffs" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[[View] || [AsTarget] <buffId>] <ab_level>";
    }

    public string GetCommandHelpText()
    {
        return "Adds or removes a buff to selected target. Negative BuffId's will remove a buff. " +
               "If preceded by AsTarget (at) buff will be applied as if target was source. " +
               "If you provide view as a parameter, it will list the current buffs on target." +
               "You can also use 'v' instead of 'view' and 'at' or 't' instead of 'AsTarget'";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var firstArg = 0;
        if (args.Length <= 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        Unit sourceUnit = character;
        Unit targetUnit = null;

        if (!(character.CurrentTarget is Unit selectedUnit))
        {
            CommandManager.SendErrorText(this, messageOutput, "No target unit selected");
            return;
        }

        targetUnit = selectedUnit;
        var userFriendlyName = string.Empty;
        if (targetUnit is Npc targetNpc)
        {
            userFriendlyName = $"@NPC_NAME({targetNpc.TemplateId})";
        }

        var a0 = args[0].ToLower();
        if (a0 == "view" || a0 == "v")
        {
            var goodBuffs = new List<Buff>();
            var badBuffs = new List<Buff>();
            var hiddenBuffs = new List<Buff>();
            selectedUnit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs, true);

            var tags = new List<uint>();
            if (goodBuffs.Count + badBuffs.Count + hiddenBuffs.Count > 0)
            {
                character.SendMessage(
                    $"[AddBuff] Listing buffs for {targetUnit.ObjId} - {targetUnit.Name} {userFriendlyName} !");
                foreach (var b in goodBuffs)
                {
                    tags.AddRange(TagsGameData.Instance.GetTagsByTargetId(TagsGameData.TagType.Buffs, b.Template.Id));
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFF00FF00{(b.Passive ? "Passive " : "")}{b.Template.Id} - {LocalizationManager.Instance.Get("buffs", "name", b.Template.Id)}|r");
                }

                foreach (var b in badBuffs)
                {
                    tags.AddRange(TagsGameData.Instance.GetTagsByTargetId(TagsGameData.TagType.Buffs, b.Template.Id));
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFFFF0000{(b.Passive ? "Passive " : "")}{b.Template.Id} - {LocalizationManager.Instance.Get("buffs", "name", b.Template.Id)}|r");
                }

                foreach (var b in hiddenBuffs)
                {
                    tags.AddRange(TagsGameData.Instance.GetTagsByTargetId(TagsGameData.TagType.Buffs, b.Template.Id));
                    CommandManager.SendNormalText(this, messageOutput,
                        $"|cFF6666FF{(b.Passive ? "Passive " : "")}{b.Template.Id} - {LocalizationManager.Instance.Get("buffs", "name", b.Template.Id)}|r");
                }

                if (tags.Count > 0)
                {
                    CommandManager.SendNormalText(this, messageOutput, $"|cFF888888Tags: {string.Join(",", tags)}|r");
                }
            }
            else
            {
                CommandManager.SendNormalText(this, messageOutput,
                    $"No buffs on {selectedUnit.ObjId} - {selectedUnit.Name}");
            }

            return;
        }

        if (a0 == "astarget" || a0 == "t" || a0 == "at")
        {
            firstArg++;
            sourceUnit = selectedUnit;
            targetUnit = character;
        }

        if (args.Length <= firstArg)
        {
            CommandManager.SendErrorText(this, messageOutput, "No buffId provided ?");
            return;
        }

        if (!int.TryParse(args[firstArg], out var buffIdInt))
        {
            CommandManager.SendErrorText(this, messageOutput, "Parse error for buffId !");
            return;
        }

        ushort abLevel = 1;
        if (args.Length > firstArg + 1)
            if (!ushort.TryParse(args[firstArg + 1], out abLevel))
                abLevel = 1;

        var buffId = (uint)Math.Abs(buffIdInt);

        var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
        if (buffTemplate == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Unknown buffId {buffId}");
            return;
        }

        if (buffIdInt > 0)
        {
            var casterObj = new SkillCasterUnit(sourceUnit.ObjId);
            var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            targetObj.ObjId = targetUnit.ObjId;

            var newBuff =
                new Buff(targetUnit, sourceUnit, casterObj, buffTemplate, null, DateTime.UtcNow) { AbLevel = abLevel };
            targetUnit.Buffs.AddBuff(newBuff);
        }

        if (buffIdInt < 0)
        {
            if (selectedUnit.Buffs.CheckBuff(buffId))
            {
                selectedUnit.Buffs.RemoveBuff(buffId);
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput, $"Target didn't have buff to remove {buffId}");
            }
        }
        else
        {
            // I think this might be zero
        }
    }
}
