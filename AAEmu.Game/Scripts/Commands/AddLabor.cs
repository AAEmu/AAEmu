using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddLabor : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "addlabor", "add_labor", "labor" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <amount> [vocationSkillId]";
        }

        public string GetCommandHelpText()
        {
            // Optional TODO: Add the values by extracting them from actability_groups ?
            return "Add or remove <amount> of labor. If [vocationSkillId] is provided, then target vocation skill also gains a amount of points.\n" +
                "(1)Alchemy, (2)Construction, (3)Cooking, (4)Handicrafts, (5)Husbandry, (6)Farming, (7)Fishing, (8)Logging, (9)Gathering, (10)Machining, " +
                "(11)Metalwork, (12)Printing, (13)Mining, (14)Masonry, (15)Tailoring, (16)Leatherwork, (17)Weaponry, (18)Carpentry, (20)Larceny, " +
                "(21)Nuian, (22)Elven, (23)Dwarven, (25)Harani, (26)Firran, (27)Warborn, (29)Nuia Dialect, (30)Haranya Dialect, " +
                "(31)Commerce, (33)Artistry, (34)Exploration";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Labor] " + CommandManager.CommandPrefix + "addlabor (target) <count> [targetSkill]");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            short count = 0;

            if ((args.Length > firstarg + 0) && (short.TryParse(args[firstarg + 0], out short argcount)))
                count = argcount;

            int vocationSkillId = 0;

            if ((args.Length > firstarg + 1) && (int.TryParse(args[firstarg + 1], out int argvocationSkillId)))
                vocationSkillId = argvocationSkillId;

            targetPlayer.ChangeLabor(count, vocationSkillId);
            if (character.Id != targetPlayer.Id)
            {
                character.SendMessage("[Labor] added {0} labor to {1}", count, targetPlayer.Name);
                targetPlayer.SendMessage("[GM] {0} has updated your labor by {1}", character.Name, count);
            }

        }
    }
}
