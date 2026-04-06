using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Run : ICommand
{
    public string[] CommandNames { get; set; } = ["run"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target)";
    }

    public string GetCommandHelpText()
    {
        return "Toggles run buff (id=3105) on the target character. If the buff is active, it removes it; otherwise, it applies it.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = character;

        if (args.Length > 0)
        {
            targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out _);
        }

        uint buffId = 3105; // Run buff id

        var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
        if (buffTemplate == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Unknown buffId {buffId}");
            return;
        }

        bool hasBuff = targetPlayer.Buffs.CheckBuff(buffId);

        if (hasBuff)
        {
            // Removing buff
            targetPlayer.Buffs.RemoveBuff(buffId);
            targetPlayer.SendDebugMessage($"Run buff removed.");
        }
        else
        {
            // Applying buff with extended duration
            var casterObj = new SkillCasterUnit(character.ObjId);
            var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            targetObj.ObjId = targetPlayer.ObjId;

            var newBuff = new Buff(targetPlayer, character, casterObj, buffTemplate, null, DateTime.UtcNow);
            targetPlayer.Buffs.AddBuff(newBuff, forcedDuration: 60000); // 1 minute
            targetPlayer.SendDebugMessage($"Run buff applied for 60 seconds.");
        }
    }
}
