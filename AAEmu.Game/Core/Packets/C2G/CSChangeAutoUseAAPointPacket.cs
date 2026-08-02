using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// which passes each field name alongside the value:
/// </remarks>
public class CSChangeAutoUseAAPointPacket() : GamePacket(CSOffsets.CSChangeAutoUseAAPointPacket, 1)
{
    public sbyte Change { get; private set; }

    public override void Read(PacketStream stream)
    {
        Change = stream.ReadSByte();

        if (Change is not 0 and not 1)
        {
            Connection.ActiveChar.SendErrorMessage(Models.Game.ErrorMessageType.Invalid);
            return;
        }

        var character = Connection.ActiveChar;
        character.AutoUseAAPoint = Change != 0;
        character.SendPacket(new G2C.SCItemTaskSuccessPacket(
            Models.Game.Items.Actions.ItemTaskType.ChangeAutoUseAaPoint,
            new Models.Game.Items.Actions.ChangeAutoUseAAPoint((byte)Change),
            []));
    }
}
