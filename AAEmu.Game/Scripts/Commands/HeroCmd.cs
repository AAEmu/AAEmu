using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Appoints and removes heroes.
/// </summary>
/// <remarks>
/// Stands in for the election, which does not exist yet: CSHeroVoting, CSHeroCandidateList and
/// CSHeroAbstain are all still parse-only. Without this there is no way to reach hero state at all, and
/// so no way to exercise anything gated on it - the Current Heroes tab, the Hero Missions tab, the
/// Dominion tab or the siege commander controls.
///
/// What it writes is what an election would write, so this does not become throwaway once voting lands.
/// </remarks>
public class HeroCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["hero"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [grant <grade> || revoke || list]";
    }

    public string GetCommandHelpText()
    {
        return
            "Appoint or remove heroes. Target defaults to your current target, else yourself.\n" +
            "  /hero grant [grade]   - appoint (grade 1 Eperium, 2 Delphinad, 3 Ayanad, 4 Erenor; default 4)\n" +
            "  /hero revoke          - remove hero status\n" +
            "  /hero list            - show the serving heroes of your nation\n" +
            "  /hero <PlayerName> grant 2\n" +
            "Retail seats six per nation: one Erenor, two Ayanad, three Delphinad.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var target = WorldManager.Instance.GetTargetOrSelf(character, args.Length > 0 ? args[0] : null, out var firstArg);
        var verb = args.Length > firstArg ? args[firstArg].ToLowerInvariant() : "list";

        var nation = HeroManager.NationOf(target);
        if (nation == 0)
        {
            CommandManager.SendNormalText(this, messageOutput, $"{target.Name} has no faction, so no nation to serve.");
            return;
        }

        switch (verb)
        {
            case "grant":
            {
                byte grade = 4;
                if (args.Length > firstArg + 1 && byte.TryParse(args[firstArg + 1], out var g) && g >= 1 && g <= 4)
                    grade = g;

                HeroManager.Instance.Grant(target, grade);
                CommandManager.SendNormalText(this, messageOutput,
                    $"{target.Name} is now a hero of nation {nation} at grade {grade}.");
                if (character.Id != target.Id)
                    target.SendMessage($"[GM] {character.Name} made you a hero.");
                break;
            }

            case "revoke":
                if (HeroManager.Instance.Revoke(target.Id))
                    CommandManager.SendNormalText(this, messageOutput, $"{target.Name} is no longer a hero.");
                else
                    CommandManager.SendNormalText(this, messageOutput, $"{target.Name} was not a hero.");
                break;

            case "list":
            {
                var heroes = HeroManager.Instance.GetHeroes(nation).ToList();
                if (heroes.Count == 0)
                {
                    CommandManager.SendNormalText(this, messageOutput, $"Nation {nation} has no heroes.");
                    break;
                }

                CommandManager.SendNormalText(this, messageOutput, $"Nation {nation} heroes:");
                foreach (var hero in heroes.OrderByDescending(h => h.Grade))
                {
                    var name = WorldManager.Instance.GetCharacterById(hero.CharacterId)?.Name ?? $"#{hero.CharacterId}";
                    CommandManager.SendNormalText(this, messageOutput, $"  {name} grade {hero.Grade} season {hero.Season}");
                }

                break;
            }

            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                break;
        }
    }
}
