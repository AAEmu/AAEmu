using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CommonFarm;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The farm-area request (CS 0x15E). <b>Answered.</b>
/// </summary>
/// <remarks>
/// <para>
/// The body is a single <c>u32</c> farm tab, and it carries <b>no position</b>: the request is "show me
/// this farm's area", asked from wherever the caller is standing, so the position is read from the
/// caller rather than from the packet.
/// </para>
/// <para>
/// The answer (SC 0x220) is <c>u32 type</c>, a <b>signed</b> <c>s32 count</c>, then that many
/// quantized world positions. Those positions are the farm's <b>shape outline</b> - the polygon of
/// the land itself - not the crops planted on it, so this handler does not fill them from the crop
/// list. No content authors that outline, so every response is a zero count, which clears the
/// reader rather than drawing a false shape. Two facts about the count are properties of the reader
/// rather than choices made here:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A count that is not positive makes the reader <b>drop the positions it was holding</b>. So a
/// zero count is not an empty answer, it is the way the area is cleared, and it is the correct
/// answer for a farm that has nothing planted in it.
/// </description></item>
/// <item><description>
/// The reader honours at most 128 entries however large the count says. The bound is applied on the
/// write side too, so a longer list is cut rather than allowed to run the reader past the end of
/// the body.
/// </description></item>
/// </list>
/// <para>
/// <b>Reachability, stated no more strongly than it is known.</b> The shipped client has no sender
/// for this opcode: it is not in the client's packet set, and the one packet-name list that did name
/// it was a copy of the zone build's list rather than the client's. Nothing in the client script
/// extract sends it either. The server therefore normally <i>pushes</i> the answer when the player
/// enters an area rather than waiting to be asked, and this handler exists for the request path —
/// for a console or tool that speaks it, and so the writer has one reachable caller.
/// </para>
/// <para>
/// Placement (CS 0x164) and removal (CS 0x163) are parsed-only; neither is a gameplay path. See
/// <c>Docs/GF-E04_FARMS.md</c> for the evidence and for what the row's three operations resolve to
/// on the shipped client.
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
        var character = Connection?.ActiveChar;
        var world = character?.ParentWorld;
        if (world == null)
        {
            Logger.Debug("ShowCommonFarmArea ({0}, type {1}) ignored: no character in world.", 0x15E, TypeValue);
            return;
        }

        PublicFarmManager.Instance.GetFarmArea(
            world, character.Transform.World.Position, out var farmType, out var positions);

        var outcome = CommonFarmShowAreaRules.Evaluate(TypeValue, (int)farmType, out var responseType);
        switch (outcome)
        {
            case CommonFarmShowAreaOutcome.Answer:
            case CommonFarmShowAreaOutcome.Clear:
                // The positions in this response are the farm's *shape outline*, not the list of
                // planted crops. They are not the same geometry, and sending crop positions here
                // would draw a wrong outline rather than a rough one. The server has no authored
                // outline to send: common_farms carries no column joining a farm area to its
                // geometry, so there is nothing to put here that is not invented. A zero count is
                // the way the reader is told to drop what it holds, so both the answered tab and the
                // mismatched tab are cleared rather than answered with a false shape.
                Logger.Debug("ShowCommonFarmArea ({0}): {1} asked for tab {2}, standing on tab {3}; "
                             + "{4} outcome, clearing the area rather than sending a shape outline. "
                             + "{5} crop position(s) are known but are not an outline.",
                    0x15E, character.Name, TypeValue, farmType, outcome, positions.Count);
                character.SendPacket(new SCShowCommonFarmPacket((uint)responseType, 0, []));
                return;

            default:
                // Off the farm, or a tab that does not exist. There is no area to describe, so
                // nothing is sent: answering would put an area on the wire that the world has not.
                Logger.Debug("ShowCommonFarmArea ({0}): {1} asked for tab {2} from a position resolving to "
                             + "'{3}'; there is no such farm area to describe.",
                    0x15E, character.Name, TypeValue, farmType);
                return;
        }
    }
}
