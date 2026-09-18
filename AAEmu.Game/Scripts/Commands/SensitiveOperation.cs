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
/// <para>
/// With feature bit 56 off — how the guard ships — every answer here says it is off. This command is how the
/// bit's behaviour is driven without a second client: <c>protect</c> opens a window, the three sensitive
/// actions are then refused, and <c>verify</c> lifts it the way an accepted second password does.
/// </para>
/// <para>
/// It is also the only way a window is opened at all: the client's account-protection packet (CS 0x19A) is a
/// state query, so no client input opens or closes one.
/// </para>
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
        "Account-protection window: state, protect or clear (GM), or verify as if the second password was accepted";

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
