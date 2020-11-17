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
            var targetPlayer = character.CurrentTarget == null ? character : character.CurrentTarget;

            var casterObj = new SkillCasterUnit(character.ObjId);

            uint buffId;
            if (!uint.TryParse(args[0], out buffId))
                return;

            var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buffTemplate == null)
                return;

            targetPlayer.Effects.AddEffect(new Effect(targetPlayer, character, casterObj, buffTemplate, null, System.DateTime.Now));
        }
    }
}
