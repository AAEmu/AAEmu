using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>GM management of the Exile System.</summary>
public class ExileCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["exile"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[on|off | open|close|auto <from> <to> | limit <from> <to> <n|auto> | suspend/resume <faction> | " +
               "reset [from to] | move <player> <factionId> | return <player> | cooldown <player> [clear] | refresh]";
    }

    public string GetCommandHelpText()
    {
        return "Without arguments: the routes, their state and this week's slots.\n" +
               "on/off: the whole exile system.\n" +
               "open/close/auto <from> <to>: force a route open, shut, or back to automatic (open while <to> is inferior and <from> is not).\n" +
               "limit <from> <to> <n|auto>: this route's weekly slots.\n" +
               "suspend/resume <faction>: take a faction out of exile in both directions, or back in.\n" +
               "reset [from to]: give back this week's used slots (all routes without arguments).\n" +
               "move <player> <factionId>: move a player now (161 pirates, 199, 200, 148...), no checks, no slot used.\n" +
               "return <player>: send an exiled player back to the faction it had before.\n" +
               "cooldown <player> [clear]: show or clear the faction change cooldown.\n" +
               "Factions: nuia, haranya, outlaw/pirate or the id.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var exile = FactionExileManager.Instance;
        if (!exile.IsStarted)
        {
            CommandManager.SendErrorText(this, messageOutput, "The exile system is not running.");
            return;
        }

        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        switch (verb)
        {
            case "" or "status":
                break;
            case "on" or "off":
                exile.SetEnabled(verb == "on");
                break;
            case "open" or "close" or "auto" when args.Length >= 3:
            {
                if (!TryRoute(args[1], args[2], out var from, out var to, messageOutput))
                    return;
                var mode = verb switch { "open" => ExileRouteMode.Open, "close" => ExileRouteMode.Closed, _ => ExileRouteMode.Automatic };
                if (!exile.SetMode(from, to, mode))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"There is no route {from} -> {to}.");
                    return;
                }

                break;
            }
            case "limit" when args.Length >= 4:
            {
                if (!TryRoute(args[1], args[2], out var from, out var to, messageOutput))
                    return;
                int? max = args[3] == "auto" ? null : int.TryParse(args[3], out var n) ? n : -1;
                if (max < 0 || !exile.SetMax(from, to, max))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Usage: limit <from> <to> <n|auto>");
                    return;
                }

                break;
            }
            case "suspend" or "resume" when args.Length >= 2:
                if (!FactionPowerCmd.TryParseFaction(args[1], out var factionId) || !exile.SetSuspended(factionId, verb == "suspend"))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Only Nuia, Haranya and the Outlaws take part in exile.");
                    return;
                }

                break;
            case "reset":
            {
                uint from = 0, to = 0;
                if (args.Length >= 3 && !TryRoute(args[1], args[2], out from, out to, messageOutput))
                    return;
                exile.ResetUsed(from, to);
                break;
            }
            case "move" when args.Length >= 3:
            {
                var target = WorldManager.Instance.GetCharacter(args[1]);
                if (target == null || !uint.TryParse(args[2], out var targetFaction))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Usage: move <online player> <factionId>");
                    return;
                }

                CommandManager.SendNormalText(this, messageOutput, exile.Exile(target, targetFaction, true, false, true));
                return;
            }
            case "return" when args.Length >= 2:
            {
                var target = WorldManager.Instance.GetCharacter(args[1]);
                if (target == null)
                {
                    CommandManager.SendErrorText(this, messageOutput, "Player not online.");
                    return;
                }

                CommandManager.SendNormalText(this, messageOutput, exile.Exile(target, 0, true, false, true));
                return;
            }
            case "cooldown" when args.Length >= 2:
            {
                var target = WorldManager.Instance.GetCharacter(args[1]);
                if (target == null)
                {
                    CommandManager.SendErrorText(this, messageOutput, "Player not online.");
                    return;
                }

                if (args.Length >= 3 && args[2] == "clear")
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        exile.ClearCooldown(target.Id) ? $"Cooldown of {target.Name} cleared." : $"{target.Name} was never exiled.");
                    return;
                }

                var ends = exile.CooldownEnds(target.Id);
                CommandManager.SendNormalText(this, messageOutput, ends == null || exile.CheckCooldown(target)
                    ? $"{target.Name} has no faction change cooldown."
                    : $"{target.Name} may change faction again after {ends:yyyy-MM-dd HH:mm} UTC.");
                return;
            }
            case "refresh":
                exile.Refresh();
                break;
            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                return;
        }

        ShowStatus(exile, messageOutput);
    }

    private bool TryRoute(string fromText, string toText, out uint from, out uint to, IMessageOutput messageOutput)
    {
        to = 0;
        if (FactionPowerCmd.TryParseFaction(fromText, out from) && FactionPowerCmd.TryParseFaction(toText, out to))
            return true;
        CommandManager.SendErrorText(this, messageOutput, "Unknown faction (nuia, haranya, outlaw or an id).");
        return false;
    }

    private void ShowStatus(FactionExileManager exile, IMessageOutput messageOutput)
    {
        var power = FactionPowerManager.Instance;
        var suspended = exile.Suspended;
        CommandManager.SendNormalText(this, messageOutput,
            $"Exile system {(exile.Enabled ? "ON" : "OFF")}, week {power.Week}. Inferior: " +
            $"{string.Join(", ", power.Inferiors.Select(FactionPowerCmd.FactionName))}" +
            (suspended.Count > 0 ? $"; suspended: {string.Join(", ", suspended.Select(FactionPowerCmd.FactionName))}" : ""));
        foreach (var route in exile.Routes())
        {
            var limit = route.MaxOverride.HasValue ? $"{route.Max} (set)" : route.Max.ToString();
            CommandManager.SendNormalText(this, messageOutput,
                $"{FactionPowerCmd.FactionName(route.From)} -> {FactionPowerCmd.FactionName(route.To)}: " +
                $"{(route.Active ? "OPEN" : "closed")} [{route.Mode}], used {route.Used}/{limit}, broker spawner {route.SpawnerId}" +
                (route.Active && !power.IsInferior(route.To)
                    ? $" - players can't use it: the client only allows exile into an inferior faction ({FactionPowerCmd.FactionName(route.To)} is not)"
                    : ""));
        }
    }
}
