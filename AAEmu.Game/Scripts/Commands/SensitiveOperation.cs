using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// The account-protection window's test surface: what the guard would tell the client, entering and leaving a
/// window, and going straight to "verified".
/// </summary>
/// <remarks>
/// The guard itself is gated by feature bit 56, so on a default server every answer here says it is off. This
/// command is how the bit's behaviour is driven without a second client: with the bit on, <c>protect</c> opens
/// a window, the three sensitive actions are then refused, and <c>verify</c> lifts it the way an accepted
/// second password does.
/// </remarks>
public class SensitiveOperation : ICommand
{
    public string[] CommandNames { get; set; } = ["sensitive", "sensitiveoperation"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "<state|protect|clear|verify>";

    public string GetCommandHelpText() =>
        "Account-protection window: state, protect, clear, or verify as if the second password was accepted";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "state";

        switch (action)
        {
            case "state":
                CommandManager.SendNormalText(this, messageOutput, SensitiveOperationGuard.Describe(character));
                break;

            case "protect":
            case "clear":
                if (SensitiveOperationGuard.TrySetProtection(character, action == "protect", out var refusal))
                    CommandManager.SendNormalText(this, messageOutput, SensitiveOperationGuard.Describe(character));
                else
                    CommandManager.SendErrorText(this, messageOutput, refusal ?? "Refused.");
                break;

            case "verify":
                SensitiveOperationGuard.OnSecondPasswordVerified(character);
                CommandManager.SendNormalText(this, messageOutput, SensitiveOperationGuard.Describe(character));
                break;

            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                break;
        }
    }
}
