using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Justice;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The arrest -> imprison-or-trial flow. The arrest skills put a shipped arrest-state buff on the
/// criminal; from there the server escorts them to their courthouse, applies the courthouse state and
/// offers the choice. Serving the sentence applies the shipped prisoner buff, whose own timeout
/// trigger releases the prisoner, so there is no release code to write here.
/// </summary>
public class JusticeManager : Singleton<JusticeManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly HashSet<uint> _escortScheduled = [];
    private readonly HashSet<uint> _awaitingReply = [];
    private readonly HashSet<uint> _defendants = [];
    private readonly HashSet<uint> _awaitingArrival = [];

    /// <summary>
    /// Called when an arrest-state buff lands on a character. The escort starts once that buff has
    /// run out, so its own shipped length sets the pace instead of a number picked here.
    /// </summary>
    public void OnArrestStateApplied(Character character)
    {
        if (character == null || !_escortScheduled.Add(character.Id))
            return;

        var arrestBuff = SkillManager.Instance.GetBuffTemplate(ArrestRules.UnderArrestBuff);
        var delay = TimeSpan.FromMilliseconds(arrestBuff?.Duration ?? 0);
        Logger.Info($"Arrest: {character.Name} ({character.Id}) will be escorted to court in {delay.TotalSeconds:0.#}s");
        TaskManager.Instance.Schedule(new ArrestEscortTask(character.Id), delay);
    }

    /// <summary>
    /// Moves an arrested character to their faction's courthouse and asks them to choose. Courts sit
    /// in the open world, so a character inside an instance is left where they are with a warning.
    /// </summary>
    public void CompleteEscort(uint characterId)
    {
        _escortScheduled.Remove(characterId);

        var character = WorldManager.Instance.GetCharacterById(characterId);
        if (character is not { IsOnline: true })
        {
            Logger.Warn($"Arrest: character {characterId} is gone before the escort - dropping");
            return;
        }

        if (character.Transform.InstanceId != WorldManager.DefaultInstanceId)
        {
            Logger.Warn($"Arrest: {character.Name} is not in the open world (instance {character.Transform.InstanceId}) - not escorting");
            return;
        }

        var alliance = character.Faction?.MotherId ?? FactionsEnum.NuiaAlliance;
        var (x, y, z) = ArrestRules.CourtPositionFor(alliance);

        character.Buffs.AddBuff(ArrestRules.ForcedMoveToCourtBuff, character);
        SkillTeleportLanding.Apply(character, 0u, 0u, WorldManager.DefaultInstanceId, x, y, z, 0f,
            TeleportReason.Lockup);

        OfferImprisonOrTrial(character);
        Logger.Info($"Arrest: {character.Name} at the {alliance} court, imprison-or-trial offered " +
                    $"(crime {character.CrimePoint}, {ArrestRules.SentenceMinutes} min)");
    }

    /// <summary>
    /// A wanted character died - retail treats that death as the summons to court: instead of a temple
    /// resurrection they come back at their courthouse as the defendant and are offered the sentence
    /// or a trial. Nothing happens to them until they resurrect.
    /// </summary>
    public void OnWantedDeath(Character character)
    {
        if (character == null || !_defendants.Add(character.Id))
            return;

        Logger.Info($"Justice: wanted character {character.Name} ({character.Id}) died - the resurrection lands them at court");
    }

    /// <summary>
    /// Courthouse the defendant resurrects at, or null for anyone else. One-shot: the flag moves to
    /// "arriving" here so a later ordinary death resurrects normally.
    /// </summary>
    public Portal TakeDefendantCourt(Character character)
    {
        if (character == null || !_defendants.Remove(character.Id))
            return null;

        if (character.Transform.InstanceId != WorldManager.DefaultInstanceId)
        {
            // Courts sit in the open world; an instanced death resurrects normally.
            Logger.Warn($"Justice: {character.Name} died wanted inside an instance - resurrecting normally");
            return null;
        }

        _awaitingArrival.Add(character.Id);
        var (x, y, z) = ArrestRules.CourtPositionFor(character.Faction?.MotherId ?? FactionsEnum.NuiaAlliance);
        return new Portal { X = x, Y = y, Z = z };
    }

    /// <summary>
    /// Called once the resurrection has placed the defendant. The courthouse state and the offer are
    /// sent here - after the vitals are restored - so the client is alive when the dialog arrives.
    /// </summary>
    public void OnResurrectionFinished(Character character)
    {
        if (character == null || !_awaitingArrival.Remove(character.Id))
            return;

        character.Buffs.AddBuff(ArrestRules.ForcedMoveToCourtBuff, character);
        OfferImprisonOrTrial(character);
        Logger.Info($"Justice: {character.Name} came back at court as the defendant " +
                    $"(crime {character.CrimePoint}, {ArrestRules.SentenceMinutes} min offered)");
    }

    private void OfferImprisonOrTrial(Character character)
    {
        character.SendPacket(new SCAskImprisonOrTrialPacket(
            (uint)Math.Max(0, (int)character.CrimePoint), ArrestRules.SentenceMinutes));
        _awaitingReply.Add(character.Id);
    }

    /// <summary>
    /// The client's answer to that offer. A reply without an open offer is ignored - the same
    /// one-shot rule the rest of the crime flow uses, so a stale or forged packet changes nothing.
    /// </summary>
    public void OnImprisonOrTrialReply(Character character, bool wantsTrial)
    {
        if (character == null)
            return;

        if (!_awaitingReply.Remove(character.Id))
        {
            Logger.Warn($"Arrest: {character.Name} replied to an imprison-or-trial offer that is not open - ignored");
            return;
        }

        if (wantsTrial)
        {
            TrialManager.Instance.StartTrial(character);
            return;
        }

        ServeSentence(character);
    }

    /// <summary>
    /// The sentence itself: the shipped prisoner buff is applied and the prisoner is moved into the
    /// jail cell. The buff's own timeout trigger releases them at the prison exit, so the length of
    /// the sentence is whatever that buff runs for - the court passes the minutes it ruled, which is
    /// the base sentence when nobody was asked to rule on it. Serving it also pays the crime off - the
    /// shipped crime-point reduction is applied and told to the client with the lockup flag raised -
    /// otherwise the prisoner walks out of jail still wanted and is arrested again on the spot.
    /// </summary>
    /// <returns>
    /// True once the prisoner is in the cell. False when the sentence could not be applied at all - the
    /// jail or the prisoner buff is not configured - because nothing was served: the caller must not
    /// treat the case as closed, since the crime was not paid and the record must stay on the books.
    /// </returns>
    public bool ServeSentence(Character character, uint minutes = ArrestRules.SentenceMinutes)
    {
        if (character == null)
            return false;

        var jail = PortalManager.Instance.GetReturnPoint(ArrestRules.JailReturnPointId);
        if (jail == null)
        {
            Logger.Warn($"Arrest: jail return point {ArrestRules.JailReturnPointId} is missing - {character.Name} is not imprisoned");
            return false;
        }

        // Resolve both halves of the sentence before touching anything: moving a prisoner into a cell
        // they have no prisoner state to leave again is worse than refusing the sentence outright.
        // The buff is what releases them at the exit, so without it there is no sentence to serve.
        var prisoner = SkillManager.Instance.GetBuffTemplate(ArrestRules.PrisonerBuff);
        if (prisoner == null)
        {
            Logger.Warn($"Arrest: prisoner buff {ArrestRules.PrisonerBuff} is missing - {character.Name} is not imprisoned");
            return false;
        }

        var sentence = minutes == 0 ? ArrestRules.SentenceMinutes : minutes;
        character.Buffs.RemoveBuff(ArrestRules.ForcedMoveToCourtBuff);

        // The shipped prisoner buff runs thirty minutes; a ruled sentence is the row the bench chose, so
        // the buff carries that length instead. Its timeout is what returns the prisoner to the exit, so
        // the forced duration is what makes the time served the sentence the court read out.
        character.Buffs.AddBuff(
            new Buff(character, character, new SkillCasterUnit(character.ObjId), prisoner, null, DateTime.UtcNow),
            forcedDuration: (int)(sentence * 60_000u));

        SkillTeleportLanding.Apply(
            character,
            ReturnTeleportRules.LoadWorldId(jail.WorldId, WorldManager.DefaultWorldTemplateId),
            jail.ZoneId,
            WorldManager.DefaultInstanceId,
            jail.X,
            jail.Y,
            jail.Z,
            jail.Yaw.DegToRad(),
            TeleportReason.Jail);
        PayOffCrime(character);
        Logger.Info($"Arrest: {character.Name} is serving {sentence} minutes in the Marianople jail");
        character.SendMessage(ChatType.System, $"You will serve {sentence} minutes.");
        return true;
    }

    /// <summary>
    /// Ends a sentence early. The prisoner buff is what makes a character a prisoner, so a release is
    /// its removal plus the trip to the prison exit - the pair the shipped buff's own timeout performs
    /// when the sentence runs out. Used by the trial test hook, which cannot wait out a sentence.
    /// </summary>
    public void ReleasePrisoner(Character character)
    {
        if (character == null)
            return;

        character.Buffs.RemoveBuff(ArrestRules.PrisonerBuff);

        var exit = PortalManager.Instance.GetReturnPoint(ArrestRules.PrisonExitReturnPointId);
        if (exit == null)
        {
            Logger.Warn($"Arrest: prison exit return point {ArrestRules.PrisonExitReturnPointId} is missing - " +
                        $"{character.Name} keeps their buff off but stays in the cell");
            return;
        }

        SkillTeleportLanding.Apply(
            character,
            ReturnTeleportRules.LoadWorldId(exit.WorldId, WorldManager.DefaultWorldTemplateId),
            exit.ZoneId,
            WorldManager.DefaultInstanceId,
            exit.X,
            exit.Y,
            exit.Z,
            exit.Yaw.DegToRad(),
            TeleportReason.Lockup);
        Logger.Info($"Arrest: {character.Name} was released from the cell");
    }

    /// <summary>
    /// Pays down the crime the sentence was served for and tells the client, so the wanted state and
    /// the HUD agree with what the court just did. The amount is the shipped crime-point reduction
    /// (see <see cref="ArrestRules.ServedCrimePointReduction"/>).
    /// </summary>
    private static void PayOffCrime(Character character)
    {
        var before = character.CrimePoint;
        character.CrimePoint = (short)Math.Clamp(
            (long)character.CrimePoint + ArrestRules.ServedCrimePointReduction, 0L, short.MaxValue);

        character.SendPacket(new SCCrimeChangedPacket(ArrestRules.ServedCrimePointReduction, character.CrimePoint,
            character.CrimeRecord, crimeScore: 0, isLockupAndImprison: true));

        Logger.Info($"Justice: {character.Name} paid off the crime that was served " +
                    $"(crime points {before} -> {character.CrimePoint})");
    }

    /// <summary>
    /// Somebody left the world. Every list here holds one-shot promises to a live session, so the
    /// character is dropped from all of them - a stale entry would block the next arrest or the next
    /// imprison-or-trial offer for a character that is no longer there.
    /// </summary>
    public void OnCharacterLogout(Character character)
    {
        if (character == null)
            return;

        _escortScheduled.Remove(character.Id);
        _awaitingReply.Remove(character.Id);
        _defendants.Remove(character.Id);
        _awaitingArrival.Remove(character.Id);

        TrialManager.Instance.OnCharacterLogout(character);
    }

    /// <summary>True while the character is serving a sentence - a prisoner is locked out of instances.</summary>
    public static bool IsPrisoner(Character character) =>
        character != null && character.Buffs.CheckBuff(ArrestRules.PrisonerBuff);
}
