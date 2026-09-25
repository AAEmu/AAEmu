using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class OpenPortalEffect : EffectTemplate
{
    public float Distance { get; set; }

    /// <summary>NPC template used for the walk-in portal at the owner's position.</summary>
    public uint EnterPortalNpcId { get; set; }

    /// <summary>NPC template used for the destination marker.</summary>
    public uint ExitPortalNpcId { get; set; }

    /// <summary>Content-controlled faction permission flag for the created portal.</summary>
    public bool FactionPermission { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character portalOwner || skillObject is not SkillObjectUnk1 portalInfo)
            return;

        // The client may name any position; the portal only opens where the owner can still reach it.
        if (!OpenPortalRules.IsWithinOpenDistance(
                new Vector3(portalInfo.X, portalInfo.Y, portalInfo.Z),
                portalOwner.Transform.World.Position,
                Distance))
        {
            return;
        }

        PortalManager.Instance.OpenPortal(portalOwner, portalInfo, this);
    }
}
