using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Music;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The "Play Score" special effect: announce the performance to the neighbours and put the
/// instrument's buff on the player.
/// </summary>
/// <remarks>
/// The instrument is whatever content says is one and the player may use: the instrument doodad
/// they are attached to (the grand piano they sat down at) wins over an instrument item in the
/// musical slot, and both are read from <c>instrument_sounds</c> through
/// <see cref="InstrumentSoundGameData"/> — no category switch, no buff id in code.
///
/// A player who may not play through a placed instrument is refused: no MIDI announcement and no
/// buff leave the server, so a stranger at somebody's piano changes nothing. When nothing in reach
/// is an instrument at all, the play is refused the same way, with the shipped
/// <see cref="ErrorMessageType.MustEquipInstrumentItem"/> message — the same requirement the client
/// itself evaluates before allowing the skill (a musical-slot weapon has to carry an
/// <c>instrument_sounds</c> row, see <c>UnitReqs</c>).
///
/// The buff is applied only when it is not on the player already, so a repeated play of the same
/// instrument applies it exactly once. The MIDI is announced on every play, including one that
/// finds the buff already on, so a pause and a score change still reach the neighbours.
/// </remarks>
public class PlayUserMusic : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.PlayUserMusic;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (target is not Character player)
            return;

        // Where they are playing: the instrument doodad they are attached to, if content says so.
        var attached = player.Bonding?.GetOwner();
        var placed = default(InstrumentSoundGameData.InstrumentSound);
        var placedIsInstrument = attached != null &&
                                 InstrumentSoundGameData.Instance.TryGetDoodad(attached.TemplateId, out placed);

        // What they are holding: the item in the musical equipment slot, if content says it is an instrument.
        var equipped = player.Inventory?.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Musical);
        var held = default(InstrumentSoundGameData.InstrumentSound);
        var heldIsInstrument = equipped != null &&
                               InstrumentSoundGameData.Instance.TryGetItem(equipped.TemplateId, out held);

        var buffs = player.Buffs;
        var decision = InstrumentPlayRules.Resolve(
            placedIsInstrument,
            placedIsInstrument && MusicInstrumentAccess.MayPlayThrough(player, attached),
            placed.BuffId,
            placed.BuffId != 0 && buffs?.CheckBuff(placed.BuffId) == true,
            heldIsInstrument,
            held.BuffId,
            held.BuffId != 0 && buffs?.CheckBuff(held.BuffId) == true);

        switch (decision.Outcome)
        {
            case InstrumentPlayOutcome.RefusedNotYours:
                // Definitive: neither the announcement nor the buff is sent, and they are told why.
                Logger.Warn("Player {0} ({1}) tried to play through instrument doodad {2}, which they may not use",
                    player.Name, player.Id, attached.TemplateId);
                player.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                return;

            case InstrumentPlayOutcome.RefusedNoInstrument:
                Logger.Warn(
                    "Player {0} ({1}) played a score without an instrument: doodad {2} and item {3} carry no instrument_sounds row",
                    player.Name, player.Id, attached?.TemplateId ?? 0, equipped?.TemplateId ?? 0);
                player.SendErrorMessage(ErrorMessageType.MustEquipInstrumentItem);
                return;

            case InstrumentPlayOutcome.AlreadyApplied:
                // The buff stays for the whole performance, including a pause. A resumed score or a
                // different one still has to be announced; only the buff is once.
                Logger.Trace("Player {0} ({1}) is already playing their {2}", player.Name, player.Id, decision.Source);
                player.BroadcastPacket(
                    new SCSendUserMusicPacket(player.ObjId, player.Name, MusicManager.Instance.GetMidiCache(player.Id)),
                    true);
                return;
        }

        // Applied: tell the neighbours what they are hearing, then put the instrument's buff on them.
        player.BroadcastPacket(
            new SCSendUserMusicPacket(player.ObjId, player.Name, MusicManager.Instance.GetMidiCache(player.Id)),
            true);

        if (decision.BuffId != 0 && buffs != null && !buffs.CheckBuff(decision.BuffId))
            buffs.AddBuff(decision.BuffId, caster ?? player);
    }
}
