using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Models.Game.Skills;
using System.CodeDom;

namespace AAEmu.Game.Scripts.Commands
{
    class AddBuff : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "usebuff", "add_buff", "addbuff" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "addbuff <buffid>";
        }

        public void Execute(Character character, string[] args)
        {
            int argsIdx = 0;
            Unit source = character;
            Unit target = character.CurrentTarget == null ? character : (Unit)character.CurrentTarget;

            if (target == null) return;

            if (args[0] == "target")
            {
                var temp = source;
                source = target;
                target = temp;
                argsIdx++;
            }

            var casterObj = new SkillCasterUnit(character.ObjId);
            var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            targetObj.ObjId = target.ObjId;


            uint buffId;
            if (!uint.TryParse(args[0], out buffId))
                return;

            var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buffTemplate == null)
                return;

            target.Effects.AddEffect(new Effect(target, character, casterObj, buffTemplate, null, System.DateTime.Now));
        }
    }
}
