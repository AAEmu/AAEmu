using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Scripts.Commands
{
    public class UseSkill : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "useskill", "test_skill", "testskill" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "usekill <skillId>";
        }

        public void Execute(Character character, string[] args)
        {
            if (args[0] != "target")
            {
                var targetPlayer = character.CurrentTarget == null ? character : character.CurrentTarget;
                var casterObj = new SkillCasterUnit(character.ObjId);
                var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                targetObj.ObjId = targetPlayer.ObjId;
                var skillObject = new SkillObject();

                uint skillId;
                if (!uint.TryParse(args[0], out skillId))
                    return;

                var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
                if (skillTemplate == null)
                    return;

                var useSkill = new Skill(skillTemplate);
                TaskManager.Instance.Schedule(new UseSkillTask(useSkill, character, casterObj, targetPlayer, targetObj, skillObject), TimeSpan.FromMilliseconds(0));
            }
            else
            {
                var target = (Unit)character.CurrentTarget;
                if (target == null)
                    return;

                var casterObj = new SkillCasterUnit(target.ObjId);
                var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                targetObj.ObjId = character.ObjId;
                var skillObject = new SkillObject();

                uint skillId;
                if (!uint.TryParse(args[1], out skillId))
                    return;

                var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
                if (skillTemplate == null)
                    return;

                var useSkill = new Skill(skillTemplate);
                TaskManager.Instance.Schedule(new UseSkillTask(useSkill, target, casterObj, character, targetObj, skillObject), TimeSpan.FromMilliseconds(0));
            }
        }
    }
}
