//! Crime / trial / bounty packets (`Core/Packets/C2G/CS*Trial*`, `CS*Jury*`, `CSReportCrime`,
//! `CSNaviOpenBounty`, ... and the matching `Core/Packets/G2C` packets).

use aaemu_commons::PacketStream;

use super::{active_char, cs_packet, sc_packet};
use crate::models::transform::convert_long;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::systems::social::crime::BountyDescription;
use crate::systems::social::util;

/// `SCBotSuspectReportedPacket`, `SCCharacterResurrectedPacket`, `SCForceAttackSetPacket`,
/// `SCTeleportUnitPacket`: the skills subsystem's definitions (one per opcode).
pub use crate::systems::skills::packets::SCForceAttackSetPacket;
pub use crate::systems::skills::packets_special::{
    SCBotSuspectReportedPacket, SCCharacterResurrectedPacket, SCTeleportUnitPacket,
};

// ---------------------------------------------------------------------------------------------
// S->C
// ---------------------------------------------------------------------------------------------

sc_packet!(
    /// `SCAskImprisonOrTrialPacket(crimeValue, jailMinutes)`.
    SCAskImprisonOrTrialPacket { crime_value: u32, jail_minutes: i32 }
    |self, s| { s.write_u32(self.crime_value).write_i32(self.jail_minutes); }
);

sc_packet!(
    /// `SCBotSuspectArrestedPacket(reporter, suspect)`.
    SCBotSuspectArrestedPacket { reporter: String, suspect: String }
    |self, s| { s.write_str(&self.reporter).write_str(&self.suspect); }
);

sc_packet!(
    /// `SCBountyListPacket(total, count, bounties)`: at most `count` entries are written.
    SCBountyListPacket { total: i32, count: u8, bounties: Vec<BountyDescription> }
    |self, s| {
        s.write_i32(self.total).write_u8(self.count);
        let mut c = 0;
        for b in &self.bounties {
            c += 1;
            if c > i32::from(self.count) {
                break;
            }
            b.write(s);
        }
    }
);

sc_packet!(
    /// `SCChangeJuryOKCountPacket(count, total)` (opcode `SCChangeJuryOkCountPacket`).
    SCChangeJuryOkCountPacket { count: i32, total: i32 }
    |self, s| { s.write_i32(self.count).write_i32(self.total); }
);

sc_packet!(
    /// `SCChangeJuryVerdictCountPacket(count, total)`.
    SCChangeJuryVerdictCountPacket { count: i32, total: i32 }
    |self, s| { s.write_i32(self.count).write_i32(self.total); }
);

sc_packet!(
    /// `SCChangeTrialStatePacket(trialId, state, curJury, remainTime)`.
    SCChangeTrialStatePacket { trial_id: u32, state: u8, cur_jury: i32, remain_time: u32 }
    |self, s| {
        s.write_u32(self.trial_id)
            .write_u8(self.state)
            .write_i32(self.cur_jury)
            .write_u32(self.remain_time);
    }
);

sc_packet!(
    /// `SCCrimeChangedPacket(points, crimePoints, infamyPoints, crimeState)`.
    SCCrimeChangedPacket { points: i32, crime_points: i16, infamy_points: i32, crime_state: i16 }
    |self, s| {
        s.write_i32(self.points)
            .write_i16(self.crime_points)
            .write_i32(self.infamy_points)
            .write_i16(self.crime_state);
    }
);

sc_packet!(
    /// `SCCrimeDataPacket(defendantId, defendantName, defendantRace, defendantFaction, trialId,
    /// sentenceTimeInMs, judgeId)`.
    SCCrimeDataPacket {
        defendant_id: u32,
        defendant_name: String,
        defendant_race: u8,
        defendant_faction: u32,
        trial_id: u32,
        sentence_time_ms: i32,
        judge_id: u32,
    }
    |self, s| {
        s.write_u32(self.defendant_id)
            .write_str(&self.defendant_name)
            .write_u8(self.defendant_race)
            .write_u32(self.defendant_faction)
            .write_u32(self.trial_id)
            .write_i32(self.sentence_time_ms)
            .write_bc(self.judge_id);
    }
);

/// One `CrimeEvent` of `SCCrimeRecordsPacket` with the values its C# `Write` looks up
/// (`WorldManager.GetCharacterById(victim) ?? Character.Load(victim)`, `NameManager`), resolved
/// by `CrimeManager::record_entry` before the packet is built.
#[derive(Debug, Clone, PartialEq)]
pub struct CrimeRecordEntry {
    pub id: u32,
    pub victim: u32,
    /// `victim?.Name ?? string.Empty`.
    pub victim_name: String,
    pub reporter: u32,
    /// `NameManager.GetCharacterName(reporter)`; `None` (a null string) makes the C#
    /// `Write(string)` throw inside its own try/catch, so nothing (not even the length) is written.
    pub reporter_name: Option<String>,
    /// `victim?.Faction?.Id ?? 0`.
    pub victim_faction: u32,
    /// `victim?.Expedition?.Id ?? 0`.
    pub victim_expedition: u32,
    pub crime_kind: u8,
    pub doodad_template: u32,
    pub used_skill_id: u32,
    pub position: (f32, f32, f32),
    pub msg: String,
    /// `ReportTime` (unix seconds).
    pub report_time: i64,
}

impl CrimeRecordEntry {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_u32(self.victim)
            .write_str(&self.victim_name)
            .write_u32(self.reporter);
        if let Some(n) = &self.reporter_name {
            s.write_str(n);
        }
        s.write_u32(self.victim_faction)
            .write_u32(self.victim_expedition)
            .write_u8(self.crime_kind)
            .write_u32(self.doodad_template)
            .write_u32(self.used_skill_id)
            .write_i64(convert_long(self.position.0))
            .write_i64(convert_long(self.position.1))
            .write_f32(self.position.2)
            .write_str(&record_msg(&self.msg))
            .write_i64(self.report_time.max(0));
    }
}

/// `string.IsNullOrWhiteSpace(msg) ? "" : msg.Substring(0, Math.Min(msg.Length, 200))` (UTF-16
/// units; a split surrogate pair becomes U+FFFD like .NET's UTF-8 encoder does).
fn record_msg(msg: &str) -> String {
    if msg.chars().all(char::is_whitespace) {
        return String::new();
    }
    let units: Vec<u16> = msg.encode_utf16().take(200).collect();
    String::from_utf16_lossy(&units)
}

sc_packet!(
    /// `SCCrimeRecordsPacket(trialId, unknown1, totalCount, thisCount, evidenceEntries)`.
    SCCrimeRecordsPacket {
        trial_id: u32,
        unknown1: u32,
        total_count: i32,
        this_count: i32,
        entries: Vec<CrimeRecordEntry>,
    }
    |self, s| {
        s.write_u32(self.trial_id)
            .write_u32(self.unknown1)
            .write_i32(self.total_count)
            .write_i32(self.this_count);
        for e in &self.entries {
            e.write(s);
        }
    }
);

sc_packet!(
    /// `SCCriminalArrestedPacket(criminalObjId, criminal, arrestor)` (the obj id is a plain
    /// `uint`, not a bc).
    SCCriminalArrestedPacket { criminal_obj_id: u32, criminal: String, arrestor: String }
    |self, s| {
        s.write_u32(self.criminal_obj_id)
            .write_str(&self.criminal)
            .write_str(&self.arrestor);
    }
);

sc_packet!(
    /// `SCInviteJuryPacket(defendantName, trial)`.
    SCInviteJuryPacket { defendant_name: String, trial: u32 }
    |self, s| { s.write_str(&self.defendant_name).write_u32(self.trial); }
);

sc_packet!(
    /// `SCJuryBeSeatedPacket(isWest, trial, court, juryNumber)`.
    SCJuryBeSeatedPacket { is_west: bool, trial: u32, court: i32, jury_number: i32 }
    |self, s| {
        s.write_bool(self.is_west)
            .write_u32(self.trial)
            .write_i32(self.court)
            .write_i32(self.jury_number);
    }
);

sc_packet!(
    /// `SCJuryPointChangedPacket(juryPoint)`.
    SCJuryPointChangedPacket { jury_point: i32 }
    |self, s| { s.write_i32(self.jury_point); }
);

sc_packet!(
    /// `SCJuryWaitStatusPacket(count, total, sentenceTimeInMs)`.
    SCJuryWaitStatusPacket { count: i32, total: i32, sentence_time_ms: i32 }
    |self, s| {
        s.write_i32(self.count)
            .write_i32(self.total)
            .write_i32(self.sentence_time_ms);
    }
);

sc_packet!(
    /// `SCJuryWaitingNumberPacket(waitingNumber)`.
    SCJuryWaitingNumberPacket { waiting_number: i32 }
    |self, s| { s.write_i32(self.waiting_number); }
);

sc_packet!(
    /// `SCRulingClosedPacket()` (empty).
    SCRulingClosedPacket {}
    |self, s| {}
);

sc_packet!(
    /// `SCRulingStatusPacket(count, total, sentenceType, sentenceTime)`.
    SCRulingStatusPacket { count: i32, total: i32, sentence_type: u8, sentence_time: i32 }
    |self, s| {
        s.write_i32(self.count)
            .write_i32(self.total)
            .write_u8(self.sentence_type)
            .write_i32(self.sentence_time);
    }
);

sc_packet!(
    /// `SCSummonDefendantPacket(trial)`.
    SCSummonDefendantPacket { trial: u32 }
    |self, s| { s.write_u32(self.trial); }
);

sc_packet!(
    /// `SCSummonJuryPacket(trial, court, juryNumber)`.
    SCSummonJuryPacket { trial: u32, court: u32, jury_number: i32 }
    |self, s| {
        s.write_u32(self.trial)
            .write_u32(self.court)
            .write_i32(self.jury_number);
    }
);

sc_packet!(
    /// `SCSuspectGoingBotTrial(type, type2, kicked)`.
    SCSuspectGoingBotTrial { kind: u32, kind2: u32, kicked: bool }
    |self, s| {
        s.write_u32(self.kind)
            .write_u32(self.kind2)
            .write_bool(self.kicked);
    }
);

/// `TeleportReason` values used by the trial system.
pub mod teleport_reason {
    pub const LOCKUP: u8 = 2;
    pub const DEFENDANT: u8 = 3;
    pub const JURY: u8 = 4;
    pub const JAIL: u8 = 5;
}

sc_packet!(
    /// `SCTrialAudienceJoinedPacket(trialId, bc, audienceName)`.
    SCTrialAudienceJoinedPacket { trial_id: u32, bc: u32, audience_name: String }
    |self, s| {
        s.write_u32(self.trial_id)
            .write_bc(self.bc)
            .write_str(&self.audience_name);
    }
);

sc_packet!(
    /// `SCTrialAudienceLeftPacket(bc, audienceName)`.
    SCTrialAudienceLeftPacket { bc: u32, audience_name: String }
    |self, s| { s.write_bc(self.bc).write_str(&self.audience_name); }
);

sc_packet!(
    /// `SCTrialCanceledPacket(trialId)`.
    SCTrialCanceledPacket { trial_id: u32 }
    |self, s| { s.write_u32(self.trial_id); }
);

sc_packet!(
    /// `SCTrialInfoPacket(playerId, crimePoint, arrest, acceptGuilty, acceptTrial, notGuilty,
    /// guilty, total, cur, crimeRecordCount, botReport)`.
    SCTrialInfoPacket {
        player_id: u32,
        crime_point: i32,
        arrest: i32,
        accept_guilty: i32,
        accept_trial: i32,
        not_guilty: i32,
        guilty: i32,
        total: i32,
        cur: i32,
        crime_record_count: i32,
        bot_report: i32,
    }
    |self, s| {
        s.write_u32(self.player_id)
            .write_i32(self.crime_point)
            .write_i32(self.arrest)
            .write_i32(self.accept_guilty)
            .write_i32(self.accept_trial)
            .write_i32(self.not_guilty)
            .write_i32(self.guilty)
            .write_i32(self.total)
            .write_i32(self.cur)
            .write_i32(self.crime_record_count)
            .write_i32(self.bot_report);
    }
);

sc_packet!(
    /// `SCTrialWaitStatusPacket(order, sentenceTimeInMs)`.
    SCTrialWaitStatusPacket { order: u32, sentence_time_ms: i32 }
    |self, s| { s.write_u32(self.order).write_i32(self.sentence_time_ms); }
);

sc_packet!(
    /// `SCUnitBountyMoneyPacket(objId, moneyAmount)`.
    SCUnitBountyMoneyPacket { obj_id: u32, money_amount: i64 }
    |self, s| { s.write_bc(self.obj_id).write_i64(self.money_amount); }
);

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

cs_packet!(
    /// `CSReplyImprisonOrTrialPacket`.
    CSReplyImprisonOrTrialPacket { request_trial: bool }
    read |s| { Ok(Self { request_trial: s.read_bool()? }) }
    handle |self, server, conn| {
        tracing::warn!("ReplyImprisonOrTrial, Trial: {}", self.request_trial);
        let p = active_char!(conn);
        server.systems.social.trials.reply_imprison_or_trial(server, &p, self.request_trial, util::now_ms());
        Ok(())
    }
);

cs_packet!(
    /// `CSSkipFinalStatementPacket`.
    CSSkipFinalStatementPacket { trial_id: u32 }
    read |s| { Ok(Self { trial_id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::debug!("SkipFinalStatement, Trial: {}", self.trial_id);
        let p = active_char!(conn);
        server.systems.social.trials.skip_final_statement_reply(&p, self.trial_id, util::now_ms());
        Ok(())
    }
);

cs_packet!(
    /// `CSReplyInviteJuryPacket`.
    CSReplyInviteJuryPacket { accept: bool, trial: u32 }
    read |s| { Ok(Self { accept: s.read_bool()?, trial: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::debug!("ReplyInviteJury, Accept: {}, Trial: {}", self.accept, self.trial);
        let p = active_char!(conn);
        if !server.systems.social.trials.process_trial_invite_reply(server, &p, self.accept, self.trial, util::now_ms()) {
            let name = p.data().unit.name.clone();
            tracing::debug!("{name} could not join trial: {}", self.trial);
        }
        Ok(())
    }
);

cs_packet!(
    /// `CSJurySummonedPacket` (logged only).
    CSJurySummonedPacket { trial: u32, court: i32, jury: i32 }
    read |s| { Ok(Self { trial: s.read_u32()?, court: s.read_i32()?, jury: s.read_i32()? }) }
    handle |self, server, conn| {
        tracing::warn!("JurySummoned, Trial: {}, Court: {}, Jury: {}", self.trial, self.court, self.jury);
        Ok(())
    }
);

cs_packet!(
    /// `CSJuryEndTestimonyPacket`.
    CSJuryEndTestimonyPacket { trial_id: u32, jury_id: i32 }
    read |s| { Ok(Self { trial_id: s.read_u32()?, jury_id: s.read_i32()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        let name = p.data().unit.name.clone();
        tracing::info!("JuryEndTestimony, {name}, Trial: {}, Jury: {}", self.trial_id, self.jury_id);
        server.systems.social.trials.jury_end_testimony(&p, self.trial_id, self.jury_id, util::now_ms());
        Ok(())
    }
);

cs_packet!(
    /// `CSCancelTrialPacket` (the C# dereferences a missing trial: logged here).
    CSCancelTrialPacket { trial: u32 }
    read |s| { Ok(Self { trial: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::warn!("CancelTrial, Trial: {}", self.trial);
        let p = active_char!(conn);
        server.systems.social.trials.cancel_trial(server, &p, self.trial, util::now_ms());
        Ok(())
    }
);

cs_packet!(
    /// `CSJuryVerdictPacket`.
    CSJuryVerdictPacket { trial_id: u32, jury: i32, sentence: u8 }
    read |s| { Ok(Self { trial_id: s.read_u32()?, jury: s.read_i32()?, sentence: s.read_u8()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        let name = p.data().unit.name.clone();
        tracing::info!(
            "JuryVerdict, {name}, Trial: {}, Jury: {}, Sentence: {}",
            self.trial_id, self.jury, self.sentence
        );
        server.systems.social.trials.jury_verdict(&p, self.trial_id, self.jury, self.sentence, util::now_ms());
        Ok(())
    }
);

cs_packet!(
    /// `CSReportCrimePacket`.
    CSReportCrimePacket {
        obj_id: u32,
        skill_id: u32,
        doodad_next_func_group: i32,
        doodad_func_id: u32,
        msg: String,
    }
    read |s| {
        Ok(Self {
            obj_id: s.read_bc()?,
            skill_id: s.read_u32()?,
            doodad_next_func_group: s.read_i32()?,
            doodad_func_id: s.read_u32()?,
            msg: s.read_string()?,
        })
    }
    handle |self, server, conn| {
        let reporter = active_char!(conn);
        let inst = reporter.data().unit.transform.instance_id;
        let evidence = server.world.world(inst).and_then(|w| w.doodad(self.obj_id));
        match evidence {
            Some(d) => {
                let (owner_id, owner_db_id) = {
                    let dd = d.data();
                    (dd.owner_id, dd.owner_db_id)
                };
                let criminal_name = server.names.get_character_name(owner_id).unwrap_or_default();
                let res = server
                    .systems
                    .social
                    .crimes
                    .report_crime(
                        server,
                        &reporter,
                        &d,
                        self.skill_id,
                        self.doodad_next_func_group,
                        self.doodad_func_id,
                        &self.msg,
                    )
                    .await;
                if res.is_some() {
                    tracing::debug!(
                        "ReportCrime, ObjId: {}, Msg: {}, SkillId: {}, DoodadFuncGroup: {}, Unknown3: {} (0x{:08X}). Owner {criminal_name} ({owner_id}), OwnerDbId {owner_db_id}",
                        self.obj_id, self.msg, self.skill_id, self.doodad_next_func_group, self.doodad_func_id, self.doodad_func_id
                    );
                } else {
                    tracing::warn!("ReportCrime, Report failed ObjId: {}, Msg: {}", self.obj_id, self.msg);
                }
            }
            None => {
                tracing::warn!("ReportCrime, Invalid evidence ObjId: {}, Msg: {}", self.obj_id, self.msg);
            }
        }
        Ok(())
    }
);

cs_packet!(
    /// `CSJoinTrialAudiencePacket`.
    CSJoinTrialAudiencePacket { id: u32 }
    read |s| { Ok(Self { id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::trace!("JoinTrialAudience, Id: {}", self.id);
        let p = active_char!(conn);
        server.systems.social.trials.join_trial_audience(server, &p, self.id);
        Ok(())
    }
);

cs_packet!(
    /// `CSLeaveTrialAudiencePacket` (empty body).
    CSLeaveTrialAudiencePacket {}
    read |s| { Ok(Self {}) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.trials.leave_trial_audience(server, &p);
        Ok(())
    }
);

cs_packet!(
    /// `CSRequestJuryWaitingNumberPacket` (empty body).
    CSRequestJuryWaitingNumberPacket {}
    read |s| { Ok(Self {}) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.trials.get_jury_queue_for_player(server, &p);
        Ok(())
    }
);

cs_packet!(
    /// `CSNaviOpenBountyPacket`: answers with an empty `SCBountyListPacket` (filled lists break
    /// the client's lua, see the C# comment).
    CSNaviOpenBountyPacket { obj_id: u32 }
    read |s| { Ok(Self { obj_id: s.read_bc()? }) }
    handle |self, server, conn| {
        tracing::warn!("NaviOpenBounty, ObjId: {}", self.obj_id);
        let p = active_char!(conn);
        p.send(SCBountyListPacket { total: 0, count: 0, bounties: Vec::new() });
        Ok(())
    }
);

/// Registers the crime / trial / bounty C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSReplyImprisonOrTrialPacket>(LEVEL_PLAIN, cs::CSReplyImprisonOrTrialPacket);
    reg.register::<CSSkipFinalStatementPacket>(LEVEL_PLAIN, cs::CSSkipFinalStatementPacket);
    reg.register::<CSReplyInviteJuryPacket>(LEVEL_PLAIN, cs::CSReplyInviteJuryPacket);
    reg.register::<CSJurySummonedPacket>(LEVEL_PLAIN, cs::CSJurySummonedPacket);
    reg.register::<CSJuryEndTestimonyPacket>(LEVEL_PLAIN, cs::CSJuryEndTestimonyPacket);
    reg.register::<CSCancelTrialPacket>(LEVEL_PLAIN, cs::CSCancelTrialPacket);
    reg.register::<CSJuryVerdictPacket>(LEVEL_PLAIN, cs::CSJuryVerdictPacket);
    reg.register::<CSReportCrimePacket>(LEVEL_PLAIN, cs::CSReportCrimePacket);
    reg.register::<CSJoinTrialAudiencePacket>(LEVEL_PLAIN, cs::CSJoinTrialAudiencePacket);
    reg.register::<CSLeaveTrialAudiencePacket>(LEVEL_PLAIN, cs::CSLeaveTrialAudiencePacket);
    reg.register::<CSRequestJuryWaitingNumberPacket>(
        LEVEL_PLAIN,
        cs::CSRequestJuryWaitingNumberPacket,
    );
    reg.register::<CSNaviOpenBountyPacket>(LEVEL_PLAIN, cs::CSNaviOpenBountyPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::registry::ClientPacket;
    use crate::systems::social::packets::body;

    fn ps(f: impl FnOnce(&mut PacketStream)) -> Vec<u8> {
        let mut s = PacketStream::new();
        f(&mut s);
        s.into_bytes()
    }

    #[test]
    fn simple_s2c_packets_match_csharp_write() {
        assert_eq!(
            body(&SCAskImprisonOrTrialPacket {
                crime_value: 60,
                jail_minutes: 15
            }),
            vec![60, 0, 0, 0, 15, 0, 0, 0]
        );
        assert_eq!(
            body(&SCBotSuspectArrestedPacket {
                reporter: "A".into(),
                suspect: "Bc".into()
            }),
            vec![1, 0, b'A', 2, 0, b'B', b'c']
        );
        assert_eq!(
            body(&SCBotSuspectReportedPacket {
                reporter: "".into(),
                suspect: "X".into()
            }),
            vec![0, 0, 1, 0, b'X']
        );
        assert_eq!(
            body(&SCChangeJuryOkCountPacket { count: 1, total: 5 }),
            vec![1, 0, 0, 0, 5, 0, 0, 0]
        );
        assert_eq!(
            body(&SCChangeJuryVerdictCountPacket {
                count: 2,
                total: -1
            }),
            vec![2, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]
        );
        assert_eq!(
            body(&SCChangeTrialStatePacket {
                trial_id: 0x01020304,
                state: 3,
                cur_jury: 4,
                remain_time: 300_000
            }),
            vec![4, 3, 2, 1, 3, 4, 0, 0, 0, 0xE0, 0x93, 0x04, 0x00]
        );
        assert_eq!(
            body(&SCCharacterResurrectedPacket {
                unit_id: 0x0A0B0C,
                x: 100.0,
                y: 200.0,
                z: 50.0,
                z_rot: 1.5
            }),
            ps(|s| {
                s.write_bc(0x0A0B0C)
                    .write_position(100.0, 200.0, 50.0)
                    .write_f32(1.5);
            })
        );
        assert_eq!(
            body(&SCCrimeChangedPacket {
                points: -5,
                crime_points: 45,
                infamy_points: 1000,
                crime_state: 1
            }),
            vec![0xFB, 0xFF, 0xFF, 0xFF, 45, 0, 0xE8, 0x03, 0, 0, 1, 0]
        );
        assert_eq!(
            body(&SCCrimeDataPacket {
                defendant_id: 7,
                defendant_name: "Bob".into(),
                defendant_race: 4,
                defendant_faction: 148,
                trial_id: 9,
                sentence_time_ms: 900_000,
                judge_id: 0x123456
            }),
            ps(|s| {
                s.write_u32(7)
                    .write_str("Bob")
                    .write_u8(4)
                    .write_u32(148)
                    .write_u32(9)
                    .write_i32(900_000)
                    .write_bc(0x123456);
            })
        );
        assert_eq!(
            body(&SCCriminalArrestedPacket {
                criminal_obj_id: 0x0A0B0C,
                criminal: "C".into(),
                arrestor: "A".into()
            }),
            vec![0x0C, 0x0B, 0x0A, 0x00, 1, 0, b'C', 1, 0, b'A']
        );
        assert_eq!(
            body(&SCForceAttackSetPacket {
                obj_id: 0x010203,
                on: true
            }),
            vec![3, 2, 1, 1]
        );
        assert_eq!(
            body(&SCInviteJuryPacket {
                defendant_name: "Def".into(),
                trial: 2
            }),
            vec![3, 0, b'D', b'e', b'f', 2, 0, 0, 0]
        );
        assert_eq!(
            body(&SCJuryBeSeatedPacket {
                is_west: true,
                trial: 1,
                court: 2,
                jury_number: 3
            }),
            vec![1, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0]
        );
        assert_eq!(
            body(&SCJuryPointChangedPacket { jury_point: 2 }),
            vec![2, 0, 0, 0]
        );
        assert_eq!(
            body(&SCJuryWaitStatusPacket {
                count: 1,
                total: 5,
                sentence_time_ms: 60_000
            }),
            vec![1, 0, 0, 0, 5, 0, 0, 0, 0x60, 0xEA, 0, 0]
        );
        assert_eq!(
            body(&SCJuryWaitingNumberPacket { waiting_number: 3 }),
            vec![3, 0, 0, 0]
        );
        assert!(body(&SCRulingClosedPacket {}).is_empty());
        assert_eq!(
            body(&SCRulingStatusPacket {
                count: 2,
                total: 3,
                sentence_type: 5,
                sentence_time: 60_000
            }),
            vec![2, 0, 0, 0, 3, 0, 0, 0, 5, 0x60, 0xEA, 0, 0]
        );
        assert_eq!(
            body(&SCSummonDefendantPacket { trial: 0x0102 }),
            vec![2, 1, 0, 0]
        );
        assert_eq!(
            body(&SCSummonJuryPacket {
                trial: 1,
                court: 2,
                jury_number: 5
            }),
            vec![1, 0, 0, 0, 2, 0, 0, 0, 5, 0, 0, 0]
        );
        assert_eq!(
            body(&SCSuspectGoingBotTrial {
                kind: 1,
                kind2: 2,
                kicked: false
            }),
            vec![1, 0, 0, 0, 2, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCTeleportUnitPacket {
                reason: teleport_reason::JAIL,
                error_message: 0,
                x: 1000.0,
                y: 2000.0,
                z: 100.0,
                z2: 90.0
            }),
            ps(|s| {
                s.write_u8(5)
                    .write_i16(0)
                    .write_position(1000.0, 2000.0, 100.0)
                    .write_f32(90.0);
            })
        );
        assert_eq!(
            body(&SCTrialAudienceJoinedPacket {
                trial_id: 4,
                bc: 0x0A0B0C,
                audience_name: "Au".into()
            }),
            vec![4, 0, 0, 0, 0x0C, 0x0B, 0x0A, 2, 0, b'A', b'u']
        );
        assert_eq!(
            body(&SCTrialAudienceLeftPacket {
                bc: 0x0A0B0C,
                audience_name: "Au".into()
            }),
            vec![0x0C, 0x0B, 0x0A, 2, 0, b'A', b'u']
        );
        assert_eq!(
            body(&SCTrialCanceledPacket { trial_id: 7 }),
            vec![7, 0, 0, 0]
        );
        assert_eq!(
            body(&SCTrialInfoPacket {
                player_id: 1,
                crime_point: 2,
                arrest: 3,
                accept_guilty: 4,
                accept_trial: 5,
                not_guilty: 6,
                guilty: 7,
                total: 8,
                cur: 9,
                crime_record_count: 10,
                bot_report: 11
            }),
            ps(|s| {
                s.write_u32(1);
                for v in 2..=11 {
                    s.write_i32(v);
                }
            })
        );
        assert_eq!(
            body(&SCTrialWaitStatusPacket {
                order: 0,
                sentence_time_ms: 900_000
            }),
            vec![0, 0, 0, 0, 0xA0, 0xBB, 0x0D, 0x00]
        );
        assert_eq!(
            body(&SCUnitBountyMoneyPacket {
                obj_id: 0x010203,
                money_amount: -2
            }),
            vec![3, 2, 1, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]
        );
    }

    #[test]
    fn bounty_list_writes_at_most_count_entries() {
        let b = BountyDescription {
            player_id: 5,
            player_name: "Wanted".into(),
            player_level: 50,
            zone_id: 129,
            pos: (1000.5, 2000.25, 30.0),
            bounty_amount: 1_000_000,
            crime_points: 60,
            is_live: true,
            player_race: 3,
            type184: 1,
            arrest_count: 2,
            accept_guilty_count: 3,
            accept_trial_count: 4,
            guilty_count: 5,
            not_guilty_count: 6,
            seconds: 99,
        };
        let entry = ps(|s| {
            s.write_u32(5)
                .write_str("Wanted")
                .write_u8(50)
                .write_u32(129)
                .write_i64(((1000.5f32 * 4096.0) as i64) << 32)
                .write_i64(((2000.25f32 * 4096.0) as i64) << 32)
                .write_f32(30.0)
                .write_u64(1_000_000)
                .write_i16(60)
                .write_bool(true)
                .write_u8(3)
                .write_i32(1)
                .write_i32(2)
                .write_i32(3)
                .write_i32(4)
                .write_i32(5) // guilty before notGuilty, as in C#
                .write_i32(6)
                .write_i32(99);
        });
        let p = SCBountyListPacket {
            total: 3,
            count: 1,
            bounties: vec![b.clone(), b],
        };
        let mut expected = vec![3, 0, 0, 0, 1];
        expected.extend_from_slice(&entry);
        assert_eq!(body(&p), expected);
        // The empty list CSNaviOpenBountyPacket sends.
        assert_eq!(
            body(&SCBountyListPacket {
                total: 0,
                count: 0,
                bounties: Vec::new()
            }),
            vec![0, 0, 0, 0, 0]
        );
    }

    fn entry() -> CrimeRecordEntry {
        CrimeRecordEntry {
            id: 11,
            victim: 2,
            victim_name: "Vic".into(),
            reporter: 3,
            reporter_name: Some("Rep".into()),
            victim_faction: 101,
            victim_expedition: 0,
            crime_kind: 3,
            doodad_template: 0,
            used_skill_id: 12345,
            position: (100.0, 200.0, 10.5),
            msg: "  ".into(),
            report_time: 1_700_000_000,
        }
    }

    fn entry_bytes(e: &CrimeRecordEntry, reporter: bool, msg: &str) -> Vec<u8> {
        ps(|s| {
            s.write_u32(e.id)
                .write_u32(e.victim)
                .write_str(&e.victim_name)
                .write_u32(e.reporter);
            if reporter {
                s.write_str("Rep");
            }
            s.write_u32(e.victim_faction)
                .write_u32(e.victim_expedition)
                .write_u8(e.crime_kind)
                .write_u32(e.doodad_template)
                .write_u32(e.used_skill_id)
                .write_i64(((e.position.0 * 4096.0) as i64) << 32)
                .write_i64(((e.position.1 * 4096.0) as i64) << 32)
                .write_f32(e.position.2)
                .write_str(msg)
                .write_i64(e.report_time);
        })
    }

    #[test]
    fn crime_records_match_csharp_write() {
        let e = entry();
        let p = SCCrimeRecordsPacket {
            trial_id: 9,
            unknown1: 0,
            total_count: 6,
            this_count: 1,
            entries: vec![e.clone()],
        };
        let mut expected = ps(|s| {
            s.write_u32(9).write_u32(0).write_i32(6).write_i32(1);
        });
        // Whitespace-only messages are written empty.
        expected.extend(entry_bytes(&e, true, ""));
        assert_eq!(body(&p), expected);

        // A missing reporter name (null string) writes nothing at all; long messages are cut to
        // 200 UTF-16 units.
        let long = "x".repeat(250);
        let e2 = CrimeRecordEntry {
            reporter_name: None,
            msg: long.clone(),
            ..entry()
        };
        let b = body(&SCCrimeRecordsPacket {
            trial_id: 9,
            unknown1: 0,
            total_count: 1,
            this_count: 1,
            entries: vec![e2.clone()],
        });
        assert_eq!(&b[16..], entry_bytes(&e2, false, &long[..200]).as_slice());
        // A surrogate pair split at unit 200 becomes U+FFFD.
        assert_eq!(
            record_msg(&format!("{}\u{1F600}", "a".repeat(199))),
            format!("{}\u{FFFD}", "a".repeat(199))
        );
        assert_eq!(record_msg(" hi "), " hi ");
    }

    fn decode<P: ClientPacket>(f: impl FnOnce(&mut PacketStream)) -> P {
        let mut s = PacketStream::new();
        f(&mut s);
        P::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap()
    }

    #[test]
    fn c2s_packets_decode() {
        assert_eq!(
            decode::<CSReplyImprisonOrTrialPacket>(|s| {
                s.write_bool(true);
            }),
            CSReplyImprisonOrTrialPacket {
                request_trial: true
            }
        );
        assert_eq!(
            decode::<CSSkipFinalStatementPacket>(|s| {
                s.write_u32(5);
            }),
            CSSkipFinalStatementPacket { trial_id: 5 }
        );
        assert_eq!(
            decode::<CSReplyInviteJuryPacket>(|s| {
                s.write_bool(true).write_u32(77);
            }),
            CSReplyInviteJuryPacket {
                accept: true,
                trial: 77
            }
        );
        assert_eq!(
            decode::<CSJurySummonedPacket>(|s| {
                s.write_u32(1).write_i32(2).write_i32(3);
            }),
            CSJurySummonedPacket {
                trial: 1,
                court: 2,
                jury: 3
            }
        );
        assert_eq!(
            decode::<CSJuryEndTestimonyPacket>(|s| {
                s.write_u32(1).write_i32(4);
            }),
            CSJuryEndTestimonyPacket {
                trial_id: 1,
                jury_id: 4
            }
        );
        assert_eq!(
            decode::<CSCancelTrialPacket>(|s| {
                s.write_u32(8);
            }),
            CSCancelTrialPacket { trial: 8 }
        );
        assert_eq!(
            decode::<CSJuryVerdictPacket>(|s| {
                s.write_u32(1).write_i32(2).write_u8(6);
            }),
            CSJuryVerdictPacket {
                trial_id: 1,
                jury: 2,
                sentence: 6
            }
        );
        assert_eq!(
            decode::<CSReportCrimePacket>(|s| {
                s.write_bc(0x0A0B0C)
                    .write_u32(20000)
                    .write_i32(-1)
                    .write_u32(0xDEADBEEF)
                    .write_str("he did it");
            }),
            CSReportCrimePacket {
                obj_id: 0x0A0B0C,
                skill_id: 20000,
                doodad_next_func_group: -1,
                doodad_func_id: 0xDEADBEEF,
                msg: "he did it".into()
            }
        );
        assert_eq!(
            decode::<CSJoinTrialAudiencePacket>(|s| {
                s.write_u32(5137);
            }),
            CSJoinTrialAudiencePacket { id: 5137 }
        );
        assert_eq!(
            decode::<CSLeaveTrialAudiencePacket>(|_| {}),
            CSLeaveTrialAudiencePacket {}
        );
        assert_eq!(
            decode::<CSRequestJuryWaitingNumberPacket>(|_| {}),
            CSRequestJuryWaitingNumberPacket {}
        );
        assert_eq!(
            decode::<CSNaviOpenBountyPacket>(|s| {
                s.write_bc(0x123);
            }),
            CSNaviOpenBountyPacket { obj_id: 0x123 }
        );
    }
}
