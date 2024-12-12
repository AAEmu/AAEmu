using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestCombat : ICommand
{
    public string[] CommandNames { get; set; } = ["testcombat", "test_combat"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<engaged||cleared||first_hit||text>";
    }

    public string GetCommandHelpText()
    {
        return
            "Command to test combat related packets. You can try to use cleared if you are stuck in combat for example.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        switch (args[0])
        {
            case "engaged": // TODO Battle Start
                if (character.CurrentTarget != null)
                {
                    character.SendPacket(new SCCombatEngagedPacket(character.ObjId));
                    character.SendPacket(new SCCombatEngagedPacket(character.CurrentTarget.ObjId));
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No target selected");
                }

                break;
            case "cleared": // TODO Battle End
                if (character.CurrentTarget is Unit target)
                {
                    character.IsInBattle = false;
                    target.IsInBattle = false;
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No target selected");
                }

                break;
            case "first_hit":
                if (character.CurrentTarget != null)
                {
                    character.SendPacket(new SCCombatFirstHitPacket(character.ObjId, character.CurrentTarget.ObjId,
                        0));
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No target selected");
                }

                break;
            case "text": // TODO Combat Effect
                character.SendPacket(new SCCombatTextPacket(0, character.ObjId, 0));
                break;
        }
    }
}
