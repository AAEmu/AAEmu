using System.Globalization;
using System.Text;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// WZRunCommand's native receiver stores at most 0xff bytes for this string.
/// </remarks>
public class CSRunZoneCommand() : GamePacket(CSOffsets.CSRunZoneCommand, 1)
{
    private const int NativeCommandCapacity = 0xff;

    // This exhaustive set comes from every CSRunZoneCommand vtable xref in the 10.0.2.13 client.
    private static readonly HashSet<string> NativeCommands =
    [
        "g_unit_collide_front_bound_rate",
        "g_unit_collide_rear_bound_rate",
        "g_unit_collide_side_bound_rate",
        "g_unit_collide_bottom_box_height_size_rate",
        "g_unit_collide_bottom_box_size_rate"
    ];

    public override void Read(PacketStream stream)
    {
        var command = stream.ReadString() ?? string.Empty;
        if (!IsNativeCommand(command))
        {
            Logger.Warn("Rejected CSRunZoneCommand from character={0}: {1}",
                Connection.ActiveChar?.ObjId, command);
            return;
        }

        var character = Connection.ActiveChar;
        if (character != null)
            WorldIntegration.RelayZoneCommand?.Invoke(character.ObjId, command);
    }

    private static bool IsNativeCommand(string command)
    {
        if (string.IsNullOrEmpty(command) ||
            Encoding.UTF8.GetByteCount(command) > NativeCommandCapacity ||
            !string.Equals(command, command.Trim(), StringComparison.Ordinal))
            return false;

        var separator = command.IndexOf(' ');
        if (separator <= 0 || !NativeCommands.Contains(command[..separator]))
            return false;

        var valueText = command[(separator + 1)..];
        if (valueText.Length == 0 || valueText.Any(char.IsWhiteSpace) ||
            !float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        return float.IsFinite(value);
    }
}
