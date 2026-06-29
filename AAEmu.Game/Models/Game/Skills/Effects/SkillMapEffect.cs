using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// skill_map_effects — draws a temporary skill-area marker on the world map UI (radius/texture/view time).
// Purely cosmetic client UI; carries no server-authoritative state.
public class SkillMapEffect : EffectTemplate
{
    public int ViewTime { get; set; }
    public bool UseFactionColor { get; set; }
    public bool UseUiEffect { get; set; }
    public int Radius { get; set; }
    public string TexturePath { get; set; }
    public string TextureKey { get; set; }
    public string TextureColorKey { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // TODO: push the map marker (texture/radius/view_time) to the client. There is no AAEmu packet for the
        // world-map skill overlay yet; the template data is loaded and ready once one exists.
    }
}
