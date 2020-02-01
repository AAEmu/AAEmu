using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Scripts.Commands
{
    public class ChangeLevel : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("change_level", this);
        }

        public string GetCommandLineHelp()
        {
            return "<level>";
        }

        public string GetCommandHelpText()
        {
            return "Adds experience points needed to reach target <level> (allowed range is 1-100)\n" +
                "Do note that going above the intended max level might break skills.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[ChangeLevel] /change_level <level>");
                //character.SendMessage("[ChangeLevel] level: 1-100");
                return;
            }

            byte level = 0;
            if (byte.TryParse(args[0], out byte parselevel))
            {
                level = parselevel;
            }

            if (level <= 0 && level > 100)
            {
                character.SendMessage("[ChangeLevel] level: 1-100");
                return;
            }

            var maxexptoadd = 0;

            if (character.Ability1 != AbilityType.None)
            {
                var expfora1 = ExpirienceManager.Instance.GetExpNeededToGivenLevel(character.Abilities.Abilities[character.Ability1].Exp, level);
                if (expfora1 > maxexptoadd)
                    maxexptoadd = expfora1;
            }

            if (character.Ability2 != AbilityType.None)
            {
                var expfora2 = ExpirienceManager.Instance.GetExpNeededToGivenLevel(character.Abilities.Abilities[character.Ability2].Exp, level);
                if (expfora2 > maxexptoadd)
                    maxexptoadd = expfora2;
            }

            if (character.Ability3 != AbilityType.None)
            {
                var expfora3 = ExpirienceManager.Instance.GetExpNeededToGivenLevel(character.Abilities.Abilities[character.Ability3].Exp, level);
                if (expfora3 > maxexptoadd)
                    maxexptoadd = expfora3;
            }

            var expforlevel = ExpirienceManager.Instance.GetExpForLevel(level) - character.Expirience;
            if (expforlevel > maxexptoadd)
                maxexptoadd = expforlevel;

            // Add maximum required xp to get to target levels
            if (maxexptoadd > 0)
                character.AddExp(maxexptoadd, true);

            // If the target level is bigger than player's current level, refill HP/MP and send level-up packet
            if (level > character.Level)
            {
                character.Level = level;
                character.Expirience = expforlevel;

                character.Hp = character.MaxHp;
                character.Mp = character.MaxMp;

                character.SendPacket(new SCLevelChangedPacket(character.ObjId, level));
            }
        }
    }
}
