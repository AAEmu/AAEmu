using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

// The buff tail carries its own signature above WriteBuff — its widths were right here from the start, but the
// field *identities* were not, and a signature that covers only widths is what let a literal sit in the buff id.
//
// SC_PACKET_UNIT_STATE (opcode 151). The layout was read from the client's own serializer rather than
// appearance, posture, attach, bonding, expertise and buff-record serialisers were each read in turn.
//
// The optional firstHitter, highAbility and gmmode blocks are gated on individual `flags` bits, so they
// are skipped unless a bit that selects them is set. Bit 11 is unrelated to those — it asks the client to
// play a slave's portal fx, and is set from Slave.PendingSpawnPortal.
//
// Every mutable field is read from the model that owns it. The signed world and region selectors default
// to the native -1 sentinels (local world and position-derived region), while a zero UnitStateType asks the
// client to select its actor from the template or race/gender. The three stat-group pisc arrays are the
// all-zero shape observed on the working retail NPC path.
public class SCUnitStatePacket : GamePacket
{
    private readonly Unit _unit;
    private readonly BaseUnitType _baseUnitType;

    public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 1)
    {
        _unit = unit;
        _baseUnitType = _unit switch
        {
            Character => BaseUnitType.Character,
            Npc => BaseUnitType.Npc,
            Slave => BaseUnitType.Slave,
            House => BaseUnitType.Housing,
            Transfer => BaseUnitType.Transfer,
            Mate => BaseUnitType.Mate,
            Shipyard => BaseUnitType.Shipyard,
            _ => BaseUnitType.Invalid
        };
    }

    private void WriteUnitStateCore(PacketStream stream)
    {
        var character = _unit as Character;
        var npc = _unit as Npc;

        // 1. ObjId (Bc, quantized-3)
        stream.WriteBc(_unit.ObjId);
        // 2. name
        stream.Write(_unit.Name ?? string.Empty); // test-spawned units can carry no name
        // 3-5. signed server-world placement selectors. These are not Transform's world/zone template ids.
        stream.Write(_unit.UnitStateWorldId);
        stream.Write(_unit.UnitStateRegionId);
        stream.Write(_unit.IsInGlobalWorld);

        // 6. id/type block: baseUnitType u8 + per-type ids (UnitState_SerializeIdTypeBlock)
        stream.Write((byte)_baseUnitType);
        switch (_baseUnitType)
        {
            case BaseUnitType.Character:
                stream.Write((ulong)(character?.Id ?? 0)); // charId (u64)
                stream.Write(_unit.UnitStateIdentityValue); // native identity member "v" (i64)
                break;
            case BaseUnitType.Npc:
                // NPC idType block = bc(3 objId) + templateId(i32) + ownerId(u64) + flag(i8).
                // i32 templateId, u64 ownerId, then i8 flag. The trailing flag is
                // MANDATORY. A previous change removed it, making our NPC state 1 byte short; the
                // client then over-read past the packet, tripped its stream-error flag, failed the
                // in the packet's deleting destructor while cleaning up the corrupt object
                // (EXCEPTION_ACCESS_VIOLATION at 0x15cafb). Restoring the byte re-aligns everything.
                stream.WriteBc(npc!.ObjId);               // objId (Bc)
                stream.Write(unchecked((int)npc.TemplateId)); // templateId (i32)
                stream.Write((ulong)npc.OwnerId);             // ownerId (u64)
                stream.Write(unchecked((sbyte)npc.UnitStateFlag)); // flag (i8), see Npc.UnitStateFlag
                break;
            case BaseUnitType.Slave:
                var slave = (Slave)_unit;
                stream.Write((ulong)slave.Id);                 // id (u64)
                stream.Write(unchecked((short)slave.TlId));     // tl (i16)
                stream.Write(unchecked((int)slave.TemplateId)); // templateId (i32)
                stream.Write((ulong)(slave.Summoner?.Id ?? 0)); // ownerId (u64)
                stream.Write(slave.MasterWorldId);              // masterWorldId (i8)
                break;
            case BaseUnitType.Housing:
                var house = (House)_unit;

                // buildstep is unsigned (union case 3 reads it through vtbl +0xA8), so the old
                // -BuildSteps.Count + CurrentStep went out as 65531 for a house mid-build. A finished
                // house also sent 0, which is indistinguishable from standing at step 0, and every
                // house in the world is finished (current_step -1) yet the client reported them
                // unbuilt. Sending the count of completed steps separates the two: a finished house
                // reports every step done, and a design with no steps still reports 0.
                var totalBuildSteps = house.Template?.BuildSteps.Count ?? 0;
                var buildStep = house.CurrentStep < 0 ? totalBuildSteps : house.CurrentStep;
                stream.Write(unchecked((short)house.TlId));
                stream.Write(unchecked((int)house.TemplateId)); // templateId (i32)
                stream.Write((ushort)buildStep);          // buildstep (u16)
                break;
            case BaseUnitType.Transfer:
                var transfer = (Transfer)_unit;
                stream.Write(unchecked((short)transfer.TlId));     // tl (i16)
                stream.Write(unchecked((int)transfer.TemplateId)); // templateId (i32)
                break;
            case BaseUnitType.Mate:
                // at struct +3 and +4 — the same three offsets the Npc case fills with templateId
                // and ownerId, with bc swapped for tl and no trailing flag byte. The i64+u32 pair
                // written here before was two bytes short, so the client ran off the end of the
                // packet and reported "not enough buffer for creationTime" against the equipment
                // block that follows, discarding the spawn.
                var mount = (Mate)_unit;
                stream.Write(unchecked((short)mount.TlId));     // tl (i16)
                stream.Write(unchecked((int)mount.TemplateId)); // templateId (i32)
                stream.Write((ulong)mount.OwnerId);             // ownerId (u64)
                break;
            case BaseUnitType.Shipyard:
                var shipyard = (Shipyard)_unit;
                stream.Write((ulong)shipyard.ShipyardData.Id);                 // id (u64)
                stream.Write(unchecked((int)shipyard.ShipyardData.TemplateId)); // templateId (i32)
                break;
        }

        // 7. master (owner name)
        stream.Write(_unit.OwnerId > 0 ? (NameManager.Instance.GetCharacterName(_unit.OwnerId) ?? "") : "");

        // Zone mirrors already carry authoritative Z from dedicatedserver — do NOT rewrite via
        // Game heightmap (often missing/wrong under ZoneAuthority → floating units).
        if (npc is not null && !npc.IsZoneMirror)
        {
            var referenceHeight = WorldManager.Instance.GetReferenceHeight(npc, _unit.Transform.Local.Position.X, _unit.Transform.Local.Position.Y, _unit.Transform.Local.Position.Z, _unit.Transform.ZoneId);
            _unit.Transform.Local.SetHeight(referenceHeight);
        }

        // 8. position (wpos, 11-byte v10)
        stream.WritePosition(_unit.Transform.Local.Position);
        // 9. scale
        stream.Write(_unit.Scale);
        // 10-11. level, heirLevel (i8)
        stream.Write(checked((sbyte)_unit.Level));
        stream.Write(checked((sbyte)_unit.HeirLevel));
        // 12. second level/heirLevel pair (UnitState_SerializeLevelBlock). Commercial NPC captures
        // (182/215/233) write zeros here — NOT a repeat of Level. Repeating Level was a guess.
        if (_baseUnitType == BaseUnitType.Npc)
        {
            stream.Write((sbyte)0);
            stream.Write((sbyte)0);
        }
        else
        {
            stream.Write(checked((sbyte)_unit.Level));
            stream.Write((sbyte)0);
        }
        // 13. slot array (4 × u8). Commercial NPCs always send 0xFF×4; 0×4 correlates with client
        // never logging OnUnitState for our mirrors (2026-07-19).
        if (_baseUnitType == BaseUnitType.Npc)
        {
            stream.Write((byte)0xFF);
            stream.Write((byte)0xFF);
            stream.Write((byte)0xFF);
            stream.Write((byte)0xFF);
        }
        else
        {
            stream.Write((byte)0);
            stream.Write((byte)0);
            stream.Write((byte)0);
            stream.Write((byte)0);
        }
        // 14. modelRef
        stream.Write(_unit.ModelId);
        // 15-16. equipment + appearance — Jul 18 working path: full Face+equip for zone mirrors
        // (Nuian 警备兵 screenshots). Compact Skin rewrite was a later regression.
        EquipmentSerializer.Write(stream, _unit, _baseUnitType);
        if (character is not null)
        {
            _unit.ModelParams.Race = (byte)character.Race;
            _unit.ModelParams.Gender = (byte)character.Gender;
            _unit.ModelParams.VisualRace = (byte)character.Race;
            _unit.ModelParams.VisualGender = (byte)character.Gender;
        }
        if (npc is not null && _unit.ModelParams.BodyWeight == 0f)
            _unit.ModelParams.BodyWeight = 1f;
        stream.Write(_unit.ModelParams);
        // 17. target position (Bc; 0 = none)
        stream.WriteBc(0);
        // 18-19. precise hp/mp (u64 in v10)
        stream.Write((ulong)Math.Max(0, _unit.Hp) * 100UL);
        stream.Write((ulong)Math.Max(0, _unit.Mp) * 100UL);

        // 20-22. attach / bonding / modelPosture
        // Client reads attach u8; unless 0xFF it then reads bc of the parent unit. Same shape as
        // doodads (ParentObjId + AttachPoint): without this, equipment child slaves (sails/cannons/
        // figureheads) are placed using Local offset as if it were World → invisible near origin
        // while mast/cargo doodads still show on the hull.
        var attachedPoint = AttachPointKind.None;
        GameObject attachedTo = null;
        if (character != null &&
            character.AttachedPoint is not AttachPointKind.None and not AttachPointKind.System)
        {
            attachedPoint = character.AttachedPoint;
            attachedTo = _unit.Transform?.Parent?.GameObject;
        }
        else if (_unit is Slave { AttachPointId: >= 0 } childSlave &&
                 (AttachPointKind)(byte)childSlave.AttachPointId is not AttachPointKind.None
                     and not AttachPointKind.System)
        {
            attachedPoint = (AttachPointKind)(byte)childSlave.AttachPointId;
            attachedTo = childSlave.ParentObj ?? _unit.Transform?.Parent?.GameObject;
        }

        if (attachedPoint is not AttachPointKind.None and not AttachPointKind.System &&
            attachedTo is not null)
        {
            stream.Write(unchecked((sbyte)attachedPoint));
            stream.WriteBc(attachedTo.ObjId);
        }
        else
        {
            stream.Write((sbyte)-1); // not attached
        }

        stream.Write((sbyte)-1);
        Unit.ModelPosture(stream, _unit, npc?.AnimActionId ?? 0, true);

        // 23. activeWeapon
        stream.Write(unchecked((sbyte)_unit.ActiveWeapon));

        // 24-25. learned-skill / passive-buff counts — NPCs empty (Jul 18 working path).
        uint[] skillIds = character?.Skills.Skills.Values.Select(s => s.Id).Take(sbyte.MaxValue).ToArray() ?? [];
        uint[] passiveIds = character?.Skills.PassiveBuffs.Values.Select(b => b.Id).Take(sbyte.MaxValue).ToArray() ?? [];
        stream.Write((sbyte)skillIds.Length);
        stream.Write((sbyte)passiveIds.Length);

        // 26-29. plain fields
        stream.Write(_unit.UnitStateType); // optional actor-type override (i32)
        stream.Write(character is not null ? character.Appellations.ActiveAppellation : 0u); // appellationStampId
        stream.Write(_unit.VehicleDyeing); // vechicleDyeing (i32, client spelling)
        stream.Write(_unit.IsTempFaction); // isTempFaction

        // 30-31. learned skills + passive buffs — IDs packed via pish/pisc
        stream.WritePisc(skillIds);
        stream.WritePisc(passiveIds);

        // 32. heading — building/housing (idType 3) writes a single f32 yaw (radians); every other unit
        if (_baseUnitType == BaseUnitType.Housing)
        {
            stream.Write(_unit.Transform.Local.Rotation.Z); // yaw (f32, radians)
        }
        else
        {
            var (roll, pitch, yaw) = _unit.Transform.Local.ToRollPitchYawSBytes();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
        }

        // 33. raceGender (u8 = race | gender<<4)
        stream.Write(character?.RaceGender ?? npc?.RaceGender ?? _unit.RaceGender);

        // 34. three pish/pisc groups (4 / 3 / 4) — Jul 18: all zeros. Putting faction here broke
        // enter-world after the working screenshot session; do not restore that guess.
        stream.WritePisc(0u, 0u, 0u, 0u);
        stream.WritePisc(0u, 0u, 0u);
        stream.WritePisc(0u, 0u, 0u, 0u);

        // 35-36. flags (i16) + attckFactionFlags (i8).
        // Bit 11 (0x0800): slave spawn-portal gate. Client UnitState apply copies it to unit+0x6E5C
        // (via dest=unit+8, packet byte +0x2685); Unit finalize then PlayFx(portal_spawn_fx_id).
        short flags = 0;
        if (_unit is Slave { PendingSpawnPortal: true })
            flags |= 0x0800;
        stream.Write(flags);
        stream.Write(_unit.AttackFactionFlags);

        // 38. character-only trailing block
        if (character is not null)
        {
            // expertise: 29 × { exp u32, order u8 } + nActive u8 + active u8 × n
            var abilities = character.Abilities.Values.Take(29).ToList();
            foreach (var ability in abilities)
            {
                stream.Write(ability.Exp);
                stream.Write(ability.Order);
            }
            // pad to 29 entries if the model holds fewer (the binary always serializes 29 fixed slots)
            for (var i = abilities.Count; i < 29; i++)
            {
                stream.Write(0u);          // exp
                stream.Write((byte)0);     // order
            }
            var activeAbilities = character.Abilities.GetActiveAbilities();
            var serializedActiveAbilities = activeAbilities.Take(sbyte.MaxValue).ToList();
            stream.Write((sbyte)serializedActiveAbilities.Count);
            foreach (var ability in serializedActiveAbilities)
                stream.Write((byte)ability);

            // 43. faction bc (3 raw bytes, obj +9880). Ground-truth UnitState_Serialize writes faction here
            // ONLY for characters (idType 0). NEVER write 0 — Nikes' zone-bridge draft WriteBc(0) and our
            // earlier zero made the client treat the player as unfactioned → same-colour bars / no attack.
            // Spirals DB = Elf 103; Nuian = 101. Fallback Friendly(1) only if Faction missing entirely.
            var factionBc = (uint)(character.Faction?.Id ?? 0);
            if (factionBc == 0)
            {
                factionBc = (uint)FactionsEnum.Friendly;
                Logger.Warn("SCUnitState Character {0}: Faction Id was 0/null — wrote Friendly({1})",
                    character.Name, factionBc);
            }
            stream.WriteBc(factionBc);
            stream.Write(unchecked((sbyte)character.DuelTeamType)); // duelTeamType (-1 = not duelling)
            stream.Write(unchecked((sbyte)character.Camp));  // camp
            character.VisualOptions.WriteOptions(stream); // premium/cosplay visual block
            stream.Write(character.PremiumGrade); // premium grade (u32), resolved from characters.point
            stream.Write(0);               // _pageInfos Size = 0 (empty)
            stream.Write(0);               // _selectPageIndex
            stream.Write(0);               // _extendMaxStats
            stream.Write(0);               // _applyExtendCount
            // 52. equipSlotReinforces — no presence byte on the wire (the serializer's optional groups are
            // always present), so both lists are written with a u32 Size. Empty for now.
            stream.Write(0u);              // slotInfoList Size = 0
            stream.Write(0u);              // levelEffectList Size = 0
        }
    }

    /// <summary>
    /// Shared UnitState_Serialize + buff tail (no action-state tail).
    /// Used by WZNpcState (0x002) and as the front of WZUnitState (0x007).
    /// </summary>
    public void WriteWzUnitStateAndBuffs(PacketStream stream)
    {
        WriteUnitStateCore(stream);
        WriteBuffLists(stream);
    }

    /// <summary>
    /// If type == dword_3A639138 (BSS, runtime 0), skip time/duration/maxHorzVel/maxVertVel.
    /// Prior 0x596502 was wrong — that immediate does not exist in the DLL; it forced the
    /// optional block and caused ZoneClientImpl op-7 serializer size mismatch.
    /// </summary>
    public void WriteWzBody(PacketStream stream)
    {
        WriteWzUnitStateAndBuffs(stream);

        stream.Write(0u);
    }

    public override PacketStream Write(PacketStream stream)
    {
        var body = new PacketStream();
        WriteUnitStateCore(body);

        WriteBuffLists(body);

        stream.Write(body, false);
        return stream;
    }


    #region NetBuff
    /// <summary>
    /// Buff tail shared by the client wire (SCUnitState) and the zone wire (WZUnitState / WZNpcState):
    /// three u8 counts — good, bad, hidden — each followed by its records. The caps are the client's own
    /// </summary>
    private void WriteBuffLists(PacketStream stream)
    {
        // Zone and client consume the same native buff-record tail. NPC size/tag buffs must reach
        // both: Zone uses them for AI validation and the client uses them for target-skill checks.

        var goodBuffs = new List<Buff>();
        var badBuffs = new List<Buff>();
        var hiddenBuffs = new List<Buff>();
        _unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs, false);

        WriteBuffList(stream, goodBuffs, 32);
        WriteBuffList(stream, badBuffs, 20);
        WriteBuffList(stream, hiddenBuffs, 28);
    }

    private static void WriteBuffList(PacketStream stream, List<Buff> buffs, int max)
    {
        stream.Write((byte)Math.Min(buffs.Count, max));
        foreach (var effect in buffs.Take(max))
            WriteBuff(stream, effect);
    }

    // copies the 0x58-byte wire record 1:1 into the 0x70-byte ClientBuffMan entry, so wire offsets are
    // entry offsets. That is what identifies the two id fields, which the serializer names only "id"/"type":
    //
    //                               with CryRandom(), which is what proves it is an instance handle.
    //   +0x04  buff template id   — the apply loop passes it straight to the template resolver
    //                               Create calls on *param_2, logging "invalid buff type: %u".
    //                               fills from BuffCreated packet +0x38, i.e. BuffCreatedWire's Caster.Id.
    //
    // Timing values here are milliseconds, unlike the BuffData block used by SCBuffCreated, which carries
    // deciseconds and is scaled by 10 on read.
    //
    // The template id travels as the FIRST value of the second pisc group, not as a field of its own.
    // Writing a literal 1 in that slot — mislabelled "stack" from the BuffData block, where stack really is
    // a plain u32 — made every unit's buffs arrive as buff 1, "Fatigue", a debuff. A house has exactly one
    // buff and no other packet to correct it, so every house showed it.
    private static void WriteBuff(PacketStream stream, Buff effect)
    {
        var stack = effect.Owner?.Buffs is null
            ? 1u
            : (uint)Math.Max(1, effect.Owner.Buffs.GetBuffCountById(effect.Template.BuffId));

        stream.Write(effect.Index);                       // +0x00 id { type i32 } — buff instance id
        stream.Write(effect.SkillCaster);                 // +0x08 SkillCaster
        stream.Write((ulong)(effect.Caster?.Id ?? 0));    // +0x30 casterId (u64)
        stream.Write((byte)(effect.Caster?.Level ?? 1));  // +0x38 sourceLevel (u8)
        stream.Write((ushort)effect.AbLevel);             // +0x3a sourceAbLevel (u16)
        // +0x3c duration, +0x40 elapsed, +0x44 tick (all ms), +0x48 tick index — the client recomputes
        // +0x48 as +0x40 / +0x44 when it builds the entry itself, so 0 is the correct value to send.
        stream.WritePisc(
            (uint)effect.Duration,
            (uint)effect.GetTimeElapsed(),
            (uint)effect.Tick,
            0u);
        // +0x04 buff template id, +0x4c stack, +0x50 charge, +0x54 unknown.
        stream.WritePisc(
            effect.Template.BuffId,
            stack,
            (uint)effect.Charge,
            0u);
    }
    #endregion NetBuff

    public override string Verbose()
    {
        return " - " + _baseUnitType.ToString() + " - " + _unit?.DebugName();
    }
}
