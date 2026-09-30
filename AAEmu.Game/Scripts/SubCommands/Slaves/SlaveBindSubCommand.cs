using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Slaves;

public class SlaveBindSubCommand : SubCommandBase
{
    public SlaveBindSubCommand()
    {
        Title = "[Slave Bind]";
        Description = "Seat the player on their summoned hull (Driver).";
        CallPrefix = $"{CommandManager.CommandPrefix}slave bind";
    }

    // No parameters, so PreExecute always calls this overload; the base one only prints help.
    public override void Execute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput) =>
        Execute(character, triggerArgument, new Dictionary<string, ParameterValue>(), messageOutput);

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var owner = (Character)character;
        var slave = owner.ParentWorld?.SlaveManager?.GetActiveSlaveByOwnerObjId(owner.ObjId);
        if (slave == null || slave.IsDead)
        {
            SendColorMessage(messageOutput, Color.Red, "No live summoned slave to bind");
            return;
        }

        owner.ParentWorld.SlaveManager.BindSlave(
            owner, slave.ObjId, AttachPointKind.Driver, AttachUnitReason.NewMaster, 12076);
        SendMessage(messageOutput, $"Bound to slave obj={slave.ObjId} tl={slave.TlId}");
    }
}
