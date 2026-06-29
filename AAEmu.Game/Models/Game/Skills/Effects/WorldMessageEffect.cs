using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// world_message_effects — broadcasts a world notice (PvP kill / kill-streak / hero announcements). The
// `message` column is a localized text key the client resolves.
public class WorldMessageEffect : EffectTemplate
{
    public bool ZoneGroupOnly { get; set; }
    public string Message { get; set; }
    public bool ZoneGroupWarState { get; set; }
    public int FactionScopeId { get; set; }
    public int KillStreakCount { get; set; }
    public bool KillHero { get; set; }
    public string IconKey { get; set; }
    public bool ChatMsg { get; set; }
    public bool NameWithForeignWorld { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (string.IsNullOrEmpty(Message))
            return;

        // messageType bit: 1 = also surface in chat. ChatMsg drives it; source 0 = generic world message.
        var packet = new SCWorldMessagePacket(0, ChatMsg ? (byte)1 : (byte)0, Message);
        if (packetBuilder != null)
            packetBuilder.AddPacket(packet);
        else
            // TODO: world messages should fan out zone- or world-wide (ZoneGroupOnly/FactionScopeId gate the
            // audience); broadcast to the caster's visible range until a wider broadcast path is wired.
            caster.BroadcastPacket(packet, true);
    }
}
