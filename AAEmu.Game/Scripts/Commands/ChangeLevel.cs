using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class ChangeLevel : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "setlevel", "set_level", "change_level", "changelevel", "level" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "<level>";
        }

        public string GetCommandHelpText()
        {
            return "Adds experience points needed to reach target <level> (allowed range is 1-55)\n" +
                "Do note that going above the intended max level might break skills.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Level] " + CommandManager.CommandPrefix + "set_level (target) <level>");
                //character.SendMessage("[Level] level: 1-100");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            byte level = 0;
            if (byte.TryParse(args[firstarg + 0], out byte parselevel))
            {
                level = parselevel;
            }

            if (level <= 0 && level > 55)
            {
                character.SendMessage("|cFFFF0000[Level] Allowed level range: 1-55|r");
                return;
            }

            var maxexptoadd = 0;

            if (targetPlayer.Ability1 != AbilityType.None)
            {
                var expfora1 = ExpirienceManager.Instance.GetExpNeededToGivenLevel(targetPlayer.Abilities.Abilities[targetPlayer.Ability1].Exp, level);
                if (expfora1 > maxexptoadd)
                    maxexptoadd = expfora1;
            }

            if (targetPlayer.Ability2 != AbilityType.None)
            {
                var expfora2 = ExpirienceManager.Instance.GetExpNeededToGivenLevel(targetPlayer.Abilities.Abilities[targetPlayer.Ability2].Exp, level);
                if (expfora2 > maxexptoadd)
                    maxexptoadd = expfora2;
            }

            if (targetPlayer.Ability3 != AbilityType.None)
            {
                var expfora3 = ExpirienceManager.Instance.GetExpNeededToGivenLevel(targetPlayer.Abilities.Abilities[targetPlayer.Ability3].Exp, level);
                if (expfora3 > maxexptoadd)
                    maxexptoadd = expfora3;
            }

            var expforlevel = ExpirienceManager.Instance.GetExpForLevel(level) - targetPlayer.Expirience;
            if (expforlevel > maxexptoadd)
                maxexptoadd = expforlevel;

            // Add maximum required xp to get to target levels
            if (maxexptoadd > 0)
                targetPlayer.AddExp(maxexptoadd, true);

            // If the target level is bigger than player's current level, refill HP/MP and send level-up packet
            if (level > targetPlayer.Level)
            {
                targetPlayer.Level = level;
                targetPlayer.Expirience = expforlevel;

                targetPlayer.Hp = targetPlayer.MaxHp;
                targetPlayer.Mp = targetPlayer.MaxMp;

                targetPlayer.SendPacket(new SCLevelChangedPacket(targetPlayer.ObjId, level));
            }
        }
    }
}
