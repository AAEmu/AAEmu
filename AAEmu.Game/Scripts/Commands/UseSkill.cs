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
            int argsIdx = 0;
            var source = character;
            var target = character.CurrentTarget == null ? character : character.CurrentTarget;

            if (target == null) return;

            if (args[0] != "target")
            {
                var temp = source;
                source = target;
                target = temp;
                argsIdx++;
            }
            var casterObj = new SkillCasterUnit(source.ObjId);
            var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            targetObj.ObjId = target.ObjId;
            var skillObject = new SkillObject();

            uint skillId;
            if (!uint.TryParse(args[argsIdx], out skillId))
                return;

            var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
            if (skillTemplate == null)
                return;

            var useSkill = new Skill(skillTemplate);
            TaskManager.Instance.Schedule(new UseSkillTask(useSkill, source, casterObj, target, targetObj, skillObject), TimeSpan.FromMilliseconds(0));
        }
    }
}
