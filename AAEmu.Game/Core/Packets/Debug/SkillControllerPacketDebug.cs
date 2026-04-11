using System.Globalization;
using NLog;

namespace AAEmu.Game.Core.Packets.Debug;

/// <summary>
/// Debug logging for skill-controller packets (shared by harpoon rope, gliders, and other mechanics — not harpoon-specific).
/// Uses NLog Debug level so lines reach the ColoredConsole target (AAEmu.Game/NLog.config: console minlevel Debug).
/// Opcodes (client_12_r208022): CSSkillControllerStatePacket 0x08A, CSCreateSkillControllerPacket 0x08B, SCSkillControllerStatePacket 0x06D.
/// Set <see cref="EnableVerbosePacketLogging"/> to true only while tracing; default is off (rope updates spam Debug every tick).
/// </summary>
public static class SkillControllerPacketDebug
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>When true, emits NLog Debug lines for C2S/G2C skill-controller packets. Default is off.</summary>
    public static bool EnableVerbosePacketLogging;

    public static void LogCsSkillControllerState(uint objId, byte scType, float? len, bool? teared, bool? cutouted)
    {
        if (!EnableVerbosePacketLogging)
            return;

        if (len.HasValue && teared.HasValue && cutouted.HasValue)
            Log.Debug("[SkillController][C2S CSSkillControllerState 0x08A] objId={0} scType={1} len={2} teared={3} cutouted={4}",
                objId, scType, len.Value.ToString(CultureInfo.InvariantCulture), teared.Value, cutouted.Value);
        else
            Log.Debug("[SkillController][C2S CSSkillControllerState 0x08A] objId={0} scType={1} (payload not read in this branch)", objId, scType);
    }

    public static void LogCsCreateSkillController(uint objId, byte scType, bool fallDamageImmune)
    {
        if (!EnableVerbosePacketLogging)
            return;

        Log.Debug("[SkillController][C2S CSCreateSkillController 0x08B] objId={0} scType={1} fallDamageImmune={2}", objId, scType, fallDamageImmune);
    }

    public static void LogScSkillControllerState(uint objId, byte scType, float len, bool teared, bool cutouted)
    {
        if (!EnableVerbosePacketLogging)
            return;

        Log.Debug("[SkillController][G2C SCSkillControllerState 0x06D] objId={0} scType={1} len={2} teared={3} cutouted={4}",
            objId, scType, len.ToString(CultureInfo.InvariantCulture), teared, cutouted);
    }
}
