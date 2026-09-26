using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The farm-area request (CS 0x15E). <b>Parsed and answered with nothing.</b>
/// </summary>
/// <remarks>
/// <para>
/// The body is a single <c>s32</c> farm type, and the matching answer (SC 0x220) is <c>u32 type</c>,
/// <c>s32 count</c> followed by a variable-layout position record per entry. That record is a
/// client-internal structure whose byte layout is not reproducible from the server, so the response
/// is intentionally not synthesised here.
/// </para>
/// <para>
/// <b>What was actually verified about senders</b>, so this claim is not broader than the evidence:
/// the opcode <c>0x15E</c> does not appear anywhere in the 7,737-file client Lua extract, by literal
/// or by any farm-packet name, and the retail schema records no <c>CSShowCommonFarmAreaPacket</c> at
/// all — it is a development-build-only packet. That extract does not contain the client's console
/// layer at all (no <c>cd_</c> command strings and no console-registration routine), so a
/// development console command could still send it and neither this comment nor the reviewer who
/// suggested one can be settled from the artifacts available here. <b>Open question:</b> if a
/// <c>cd_</c>-style developer command exists, the request is reachable from a console and this
/// handler should say so rather than claim no sender. Until that is resolved the honest statement is
/// the narrow one above.
/// </para>
/// <para>
/// Placement (CS 0x164) and removal (CS 0x163) are also parsed-only; neither is a gameplay path.
/// </para>
/// </remarks>
public class CSShowCommonFarmAreaPacket() : GamePacket(CSOffsets.CSShowCommonFarmAreaPacket, 1)
{
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
    }

    public override void Execute()
    {
        // No client sender found in the Lua extract, and the retail build has no such packet. Log and
        // answer nothing rather than fabricate the position-carrying response.
        Logger.Debug("ShowCommonFarmArea ({0}, type {1}) ignored: no sender found in the client Lua "
                     + "extract and absent from the retail schema.",
            0x15E, TypeValue);
    }
}
