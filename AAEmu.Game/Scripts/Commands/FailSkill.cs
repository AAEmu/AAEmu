using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class FailSkill : ICommand
{
    public void OnLoad()
    {
        string[] name = { "failskill", "fail_skill" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "<skill result> [ushort] [uint]";
    }

    public string GetCommandHelpText()
    {
        return "Force yourself to use a default skill that fails with a given error";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        int argsIdx = 0;
        Unit source = character;
        Unit target = character; // Target self

        if (args.Length == 0)
        {
            character.SendMessage("[FailSkill] " + CommandManager.CommandPrefix + "FailSkill " + GetCommandLineHelp());
            return;
        }

        if (!byte.TryParse(args[argsIdx], out var failId) || failId == 0)
        {
            character.SendMessage($"[FailSkill] not fail number (byte), must be larger than 0, your value: {args[argsIdx]}");
            return;
        }
        argsIdx++;

        ushort ushortVal = 0;
        if (args.Length > argsIdx)
        {
            if (!ushort.TryParse(args[argsIdx], out ushortVal))
            {
                character.SendMessage(
                    $"[FailSkill] not a number (ushort), your value: {args[argsIdx]}");
                return;
            }

            argsIdx++;
        }

        uint uintVal = 0;
        if (args.Length > argsIdx)
        {
            if (!uint.TryParse(args[argsIdx], out uintVal))
            {
                character.SendMessage(
                    $"[FailSkill] not a number (uint), your value: {args[argsIdx]}");
                return;
            }

            // argsIdx++;
        }

        var casterObj = new SkillCasterUnit(source.ObjId);
        var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
        targetObj.ObjId = target.ObjId;
        var skillObject = new SkillObject();
        var skillId = 2u; // using basic melee attack
        var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
        if (skillTemplate == null)
            return;

        var useSkill = new Skill(skillTemplate);
        var skillStartedPacket = new SCSkillStartedPacket(skillId, 0, casterObj, targetObj, useSkill, skillObject);
        skillStartedPacket.SetSkillResult((SkillResult)failId);
        skillStartedPacket.SetResultUShort(ushortVal);
        skillStartedPacket.SetResultUInt(uintVal);
        character.SendPacket(skillStartedPacket);
    }
}
