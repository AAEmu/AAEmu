//! Social flow tests with several connections (no MySQL: saves fail fast and are logged).

use aaemu_commons::PacketStream;

use crate::net::packet::encode_raw;
use crate::opcodes::{cs, sc};
use crate::server::GameServer;
use crate::systems::social::chat::chat_type;
use crate::systems::social::packets::body;
use crate::systems::social::packets::team::*;
use crate::systems::social::team::risky_action;
use crate::test_support::{spawn_player, test_character, test_server, TestClient};
use crate::world::PlayerRef;

struct Pc {
    c: TestClient,
    p: PlayerRef,
}

impl Pc {
    fn ops(&mut self) -> Vec<u16> {
        self.c.drain_ops()
    }

    fn frames(&mut self) -> Vec<(u16, Vec<u8>)> {
        self.c.drain()
    }

    async fn send(&mut self, server: &GameServer, opcode: u16, body: PacketStream) {
        server
            .handle_frame(&mut self.c.conn, &encode_raw(1, opcode, body.as_bytes()))
            .await
            .expect("handler");
    }
}

fn pc(server: &GameServer, id: u32, name: &str, x: f32, y: f32) -> Pc {
    let c = TestClient::new(server, id, 1000 + id);
    let p = spawn_player(server, &c, test_character(id, name, x, y), 0x100 + id);
    p.data().is_online = true;
    let mut c = c;
    c.conn.active_char = Some(p.clone());
    c.drain();
    Pc { c, p }
}

/// Drops the frames produced by spawning (players seeing each other).
fn settle(pcs: &mut [&mut Pc]) {
    for p in pcs.iter_mut() {
        p.c.drain();
    }
}

fn find(frames: &[(u16, Vec<u8>)], op: u16) -> Vec<Vec<u8>> {
    frames
        .iter()
        .filter(|(o, _)| *o == op)
        .map(|(_, b)| b.clone())
        .collect()
}

fn invite(team_id: u32, name: &str) -> PacketStream {
    let mut s = PacketStream::new();
    s.write_u32(team_id).write_bool(true).write_str(name);
    s
}

fn reply(team_id: u32, owner: u32, reject: bool) -> PacketStream {
    let mut s = PacketStream::new();
    s.write_u32(team_id)
        .write_bool(true)
        .write_u32(owner)
        .write_bool(reject)
        .write_str("")
        .write_bool(false);
    s
}

fn risky(team_id: u32, target: u32, action: u8) -> PacketStream {
    let mut s = PacketStream::new();
    s.write_u32(team_id).write_u32(target).write_u8(action);
    s
}

fn chat(t: i16, msg: &str) -> PacketStream {
    let mut s = PacketStream::new();
    s.write_i16(t)
        .write_i16(0)
        .write_i32(0)
        .write_str("")
        .write_str(msg)
        .write_u8(0)
        .write_i32(0);
    s
}

/// A forms a party with B (invite + accept). Returns the team id.
async fn form_party(server: &GameServer, a: &mut Pc, b: &mut Pc) -> u32 {
    a.send(server, cs::CSInviteToTeamPacket, invite(0, "Bob"))
        .await;
    let ask = find(&b.frames(), sc::SCAskToJoinTeamPacket);
    assert_eq!(ask.len(), 1);
    assert_eq!(
        ask[0],
        body(&SCAskToJoinTeamPacket {
            team_id: 0,
            sender_id: a.p.character_id,
            sender_name: "Al".into(),
            is_party: true,
        })
    );
    b.send(
        server,
        cs::CSReplyToJoinTeamPacket,
        reply(0, a.p.character_id, false),
    )
    .await;
    let team = server
        .systems
        .social
        .teams
        .active_team_by_unit(a.p.character_id)
        .expect("team created");
    team.id
}

#[tokio::test]
async fn risky_team_actions_require_member_owner_and_known_action() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 110.0, 100.0);
    let mut c = pc(&server, 3, "Cy", 120.0, 100.0);
    settle(&mut [&mut a, &mut b, &mut c]);
    let team_id = form_party(&server, &mut a, &mut b).await;
    settle(&mut [&mut a, &mut b, &mut c]);
    for (actor, target, action) in [
        (3, 0, risky_action::DISMISS),
        (3, 3, risky_action::LEAVE),
        (3, 2, risky_action::KICK),
        (2, 0, risky_action::DISMISS),
        (2, 1, risky_action::KICK),
        (2, 1, risky_action::LEAVE),
        (1, 0, u8::MAX),
        (1, 99, risky_action::KICK),
    ] {
        let actor = match actor {
            1 => &mut a,
            2 => &mut b,
            _ => &mut c,
        };
        actor
            .send(
                &server,
                cs::CSAskRiskyTeamActionPacket,
                risky(team_id, target, action),
            )
            .await;
        assert!(a.ops().is_empty() && b.ops().is_empty() && c.ops().is_empty());
        let team = server
            .systems
            .social
            .teams
            .active_team(team_id)
            .expect("team preserved");
        assert_eq!(team.owner_id, 1);
        assert_eq!(team.members_count(), 2);
        assert!(a.p.data().in_party && b.p.data().in_party);
        assert!(!c.p.data().in_party);
    }
    // Rejected requests also preserve both party channel memberships.
    a.send(
        &server,
        cs::CSSendChatMessagePacket,
        chat(chat_type::PARTY, "still here"),
    )
    .await;
    assert_eq!(a.ops(), vec![sc::SCChatMessagePacket]);
    assert_eq!(b.ops(), vec![sc::SCChatMessagePacket]);
    assert!(c.ops().is_empty());
    // The authenticated owner can still dismiss the party.
    a.send(
        &server,
        cs::CSAskRiskyTeamActionPacket,
        risky(team_id, 0, risky_action::DISMISS),
    )
    .await;
    for member in [&mut a, &mut b] {
        let frames = member.frames();
        assert_eq!(
            find(&frames, sc::SCTeamDismissedPacket),
            vec![body(&SCTeamDismissedPacket { team_id })]
        );
        assert_eq!(
            find(&frames, sc::SCLeavedTeamPacket),
            vec![body(&SCLeavedTeamPacket {
                team_id,
                kicked: false,
                dismissed: true
            })]
        );
        assert!(!member.p.data().in_party);
    }
    assert!(server.systems.social.teams.active_team(team_id).is_none());
}

#[tokio::test]
async fn family_changes_reject_foreign_members_and_save_authorized_snapshots() {
    use crate::systems::social::family::{Family, FamilyMember};
    use crate::systems::social::packets::family::*;
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 110.0, 100.0);
    let mut c = pc(&server, 3, "Cy", 120.0, 100.0);
    settle(&mut [&mut a, &mut b, &mut c]);
    let member = |p: &PlayerRef, role| FamilyMember {
        character: Some(p.clone()),
        id: p.character_id,
        name: p.data().unit.name.clone(),
        role,
        title: "Old".into(),
    };
    server.systems.social.families.insert(Family {
        id: 11,
        members: vec![member(&a.p, 1), member(&b.p, 0)],
        removed_members: vec![901],
    });
    server.systems.social.families.insert(Family {
        id: 22,
        members: vec![member(&c.p, 1)],
        removed_members: vec![902],
    });
    a.p.data().family = 11;
    b.p.data().family = 11;
    c.p.data().family = 22;
    let mut title = PacketStream::new();
    title.write_u32(3).write_str("Forged");
    a.send(&server, cs::CSFamilyChangeTitlePacket, title).await;
    let mut owner = PacketStream::new();
    owner.write_u32(3);
    a.send(&server, cs::CSFamilyChangeOwnerPacket, owner).await;
    assert!(a.ops().is_empty() && b.ops().is_empty() && c.ops().is_empty());
    let own = server.systems.social.families.family(11).unwrap();
    let foreign = server.systems.social.families.family(22).unwrap();
    assert_eq!(own.member(1).unwrap().role, 1);
    assert_eq!(own.removed_members, vec![901]); // Invalid requests never request a save snapshot.
    assert_eq!(foreign.member(3).unwrap().title, "Old");
    assert_eq!(foreign.member(3).unwrap().role, 1);
    assert_eq!(foreign.removed_members, vec![902]);
    let mut title = PacketStream::new();
    title.write_u32(2).write_str("Brother");
    a.send(&server, cs::CSFamilyChangeTitlePacket, title).await;
    for member in [&mut a, &mut b] {
        assert_eq!(
            member.frames(),
            vec![(
                sc::SCFamilyTitleChangedPacket,
                body(&SCFamilyTitleChangedPacket {
                    family_id: 11,
                    member_id: 2,
                    title: "Brother".into()
                })
            )]
        );
    }
    let mut updated = server.systems.social.families.family(11).unwrap();
    assert_eq!(updated.member(2).unwrap().title, "Brother");
    assert!(updated.removed_members.is_empty()); // Existing save_family took its updated snapshot.
    updated.removed_members.push(903);
    server.systems.social.families.insert(updated);
    let mut owner = PacketStream::new();
    owner.write_u32(2);
    a.send(&server, cs::CSFamilyChangeOwnerPacket, owner).await;
    let updated = server.systems.social.families.family(11).unwrap();
    assert_eq!(updated.member(1).unwrap().role, 0);
    assert_eq!(updated.member(2).unwrap().role, 1);
    assert_eq!(updated.member(2).unwrap().title, "Brother");
    assert!(updated.removed_members.is_empty());
    for member in [&mut a, &mut b] {
        assert_eq!(
            member.frames(),
            vec![
                (
                    sc::SCFamilyOwnerChangedPacket,
                    body(&SCFamilyOwnerChangedPacket {
                        family_id: 11,
                        member_id: 2
                    })
                ),
                (
                    sc::SCFamilyDescPacket,
                    body(&SCFamilyDescPacket {
                        family: updated.info()
                    })
                ),
            ]
        );
    }
    assert!(c.ops().is_empty());
    assert_eq!(
        server
            .systems
            .social
            .families
            .family(22)
            .unwrap()
            .removed_members,
        vec![902]
    );
}

#[tokio::test]
async fn team_invite_accept_chat_owner_kick_leave() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 110.0, 100.0);
    let mut c = pc(&server, 3, "Cy", 1900.0, 1900.0);
    settle(&mut [&mut a, &mut b, &mut c]);

    let team_id = form_party(&server, &mut a, &mut b).await;
    let fa = a.frames();
    let fb = b.frames();
    // Both get SCJoinedTeamPacket, the ping position and join their party chat channel.
    for f in [&fa, &fb] {
        let ops: Vec<u16> = f.iter().map(|x| x.0).collect();
        assert_eq!(
            ops,
            vec![
                sc::SCJoinedTeamPacket,
                sc::SCTeamPingPosPacket,
                sc::SCJoinedChatChannelPacket
            ],
            "{ops:x?}"
        );
    }
    let joined = &find(&fa, sc::SCJoinedTeamPacket)[0];
    let mut s = PacketStream::from_bytes(joined.clone());
    assert_eq!(s.read_u32().unwrap(), team_id);
    assert_eq!(s.read_u32().unwrap(), 1); // owner
    assert!(s.read_bool().unwrap()); // party
    assert_eq!(s.read_u8().unwrap(), 2); // party 0 count
    assert!(a.p.data().in_party && b.p.data().in_party);
    let party_chan = find(&fa, sc::SCJoinedChatChannelPacket)[0].clone();
    assert_eq!(
        &party_chan[..4],
        &[
            4,
            0,
            ((team_id << 6) & 0xFF) as u8,
            ((team_id << 6) >> 8) as u8
        ]
    );

    // Party chat reaches both members.
    a.send(
        &server,
        cs::CSSendChatMessagePacket,
        chat(chat_type::PARTY, "hi"),
    )
    .await;
    assert_eq!(a.ops(), vec![sc::SCChatMessagePacket]);
    assert_eq!(b.ops(), vec![sc::SCChatMessagePacket]);
    // C is not in a party -> error.
    c.send(
        &server,
        cs::CSSendChatMessagePacket,
        chat(chat_type::PARTY, "x"),
    )
    .await;
    assert_eq!(c.ops(), vec![sc::SCErrorMsgPacket]);
    // Raid chat in a party (team found, raid channel empty) and raid leader by a non-owner.
    b.send(
        &server,
        cs::CSSendChatMessagePacket,
        chat(chat_type::RAID_LEADER, "x"),
    )
    .await;
    assert_eq!(b.ops(), vec![sc::SCErrorMsgPacket]);

    // C joins (far away: the ObjIds are refreshed both ways).
    a.send(&server, cs::CSInviteToTeamPacket, invite(team_id, "Cy"))
        .await;
    assert_eq!(c.ops(), vec![sc::SCAskToJoinTeamPacket]);
    c.send(
        &server,
        cs::CSReplyToJoinTeamPacket,
        reply(team_id, a.p.character_id, false),
    )
    .await;
    assert_eq!(
        c.ops(),
        vec![
            sc::SCJoinedTeamPacket,
            sc::SCTeamPingPosPacket,
            sc::SCRefreshTeamMemberPacket,
            sc::SCRefreshTeamMemberPacket
        ]
    );
    assert_eq!(
        a.ops(),
        vec![sc::SCTeamMemberJoinedPacket, sc::SCRefreshTeamMemberPacket]
    );
    assert_eq!(
        b.ops(),
        vec![sc::SCTeamMemberJoinedPacket, sc::SCRefreshTeamMemberPacket]
    );
    // Note (C#): a member joining an existing team does not join the party chat channel, but
    // can still talk into it.
    c.send(
        &server,
        cs::CSSendChatMessagePacket,
        chat(chat_type::PARTY, "x"),
    )
    .await;
    assert!(c.ops().is_empty());
    assert_eq!(a.ops(), vec![sc::SCChatMessagePacket]);
    assert_eq!(b.ops(), vec![sc::SCChatMessagePacket]);

    // Owner change A -> B.
    let mut s = PacketStream::new();
    s.write_u32(team_id).write_u32(2);
    a.send(&server, cs::CSMakeTeamOwnerPacket, s).await;
    let owner_changed = body(&SCTeamOwnerChangedPacket { team_id, id: 2 });
    for x in [&mut a, &mut b, &mut c] {
        assert_eq!(
            x.frames(),
            vec![(sc::SCTeamOwnerChangedPacket, owner_changed.clone())]
        );
    }

    // A (no longer owner) cannot kick; B kicks C.
    a.send(
        &server,
        cs::CSAskRiskyTeamActionPacket,
        risky(team_id, 3, risky_action::KICK),
    )
    .await;
    assert!(a.ops().is_empty() && c.ops().is_empty());
    b.send(
        &server,
        cs::CSAskRiskyTeamActionPacket,
        risky(team_id, 3, risky_action::KICK),
    )
    .await;
    // The kicked member is removed first, so it only gets SCLeavedTeamPacket.
    assert_eq!(
        c.frames(),
        vec![(
            sc::SCLeavedTeamPacket,
            body(&SCLeavedTeamPacket {
                team_id,
                kicked: true,
                dismissed: false
            })
        )]
    );
    // Note (C#): the kicker (B) leaves its own party chat channel.
    assert_eq!(
        b.ops(),
        vec![
            sc::SCLeavedChatChannelPacket,
            sc::SCTeamMemberLeavedPacket,
            sc::SCTeamAckRiskyActionPacket
        ]
    );
    assert_eq!(
        a.ops(),
        vec![sc::SCTeamMemberLeavedPacket, sc::SCTeamAckRiskyActionPacket]
    );
    assert!(!c.p.data().in_party);

    // A leaves: a party of one is disbanded.
    a.send(
        &server,
        cs::CSAskRiskyTeamActionPacket,
        risky(team_id, 1, risky_action::LEAVE),
    )
    .await;
    let fa = a.frames();
    let fb = b.frames();
    assert_eq!(
        fa.iter().map(|x| x.0).collect::<Vec<_>>(),
        vec![sc::SCLeavedChatChannelPacket, sc::SCLeavedTeamPacket]
    );
    assert_eq!(
        fb.iter().map(|x| x.0).collect::<Vec<_>>(),
        vec![
            sc::SCTeamMemberLeavedPacket,
            sc::SCTeamAckRiskyActionPacket,
            sc::SCTeamDismissedPacket,
            sc::SCLeavedTeamPacket
        ]
    );
    assert_eq!(
        find(&fb, sc::SCLeavedTeamPacket)[0],
        body(&SCLeavedTeamPacket {
            team_id,
            kicked: false,
            dismissed: true
        })
    );
    assert!(server.systems.social.teams.active_team(team_id).is_none());
    assert!(!a.p.data().in_party && !b.p.data().in_party);
}

#[tokio::test]
async fn team_rejection_raid_conversion_offline_and_reconnect() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 110.0, 100.0);
    settle(&mut [&mut a, &mut b]);

    // Rejection: the owner gets SCRejectedTeamPacket with the target's name.
    a.send(&server, cs::CSInviteToTeamPacket, invite(0, "Bob"))
        .await;
    b.ops();
    b.send(&server, cs::CSReplyToJoinTeamPacket, reply(0, 1, true))
        .await;
    assert_eq!(
        a.frames(),
        vec![(
            sc::SCRejectedTeamPacket,
            body(&SCRejectedTeamPacket {
                name: "Bob".into(),
                party: true
            })
        )]
    );
    // A second invitation while one is pending is rejected at once.
    a.send(&server, cs::CSInviteToTeamPacket, invite(0, "Bob"))
        .await;
    a.send(&server, cs::CSInviteToTeamPacket, invite(0, "Bob"))
        .await;
    assert_eq!(a.ops(), vec![sc::SCRejectedTeamPacket]);
    b.send(&server, cs::CSReplyToJoinTeamPacket, reply(0, 1, false))
        .await;
    let team_id = server
        .systems
        .social
        .teams
        .active_team_by_unit(1)
        .expect("team")
        .id;
    a.ops();
    b.ops();

    // Convert to raid: both join the raid channel; raid chat works for every member.
    let mut s = PacketStream::new();
    s.write_u32(team_id);
    a.send(&server, cs::CSConvertToRaidTeamPacket, s).await;
    for x in [&mut a, &mut b] {
        assert_eq!(
            x.ops(),
            vec![
                sc::SCTeamBecameRaidTeamPacket,
                sc::SCJoinedChatChannelPacket
            ]
        );
    }
    b.send(
        &server,
        cs::CSSendChatMessagePacket,
        chat(chat_type::RAID, "r"),
    )
    .await;
    assert_eq!(a.ops(), vec![sc::SCChatMessagePacket]);
    assert_eq!(b.ops(), vec![sc::SCChatMessagePacket]);

    // Role / marker / looting rule / ping.
    let mut s = PacketStream::new();
    s.write_u32(team_id).write_u32(2).write_u8(2);
    b.send(&server, cs::CSSetTeamMemberRolePacket, s).await;
    assert_eq!(a.ops(), vec![sc::SCTeamMemberRoleChangedPacket]);
    b.ops();
    let mut s = PacketStream::new();
    s.write_u32(team_id)
        .write_i32(3)
        .write_u8(2)
        .write_bc(0x102);
    a.send(&server, cs::CSSetOverHeadMarkerPacket, s).await;
    let marker = body(&SCOverHeadMarkerSetPacket {
        team_id,
        index: 3,
        is_obj_id: true,
        id: 0x102,
    });
    assert_eq!(b.frames(), vec![(sc::SCOverHeadMarkerSetPacket, marker)]);
    a.ops();
    let mut s = PacketStream::new();
    s.write_u32(team_id)
        .write_u8(2)
        .write_u8(4)
        .write_u32(2)
        .write_bool(false)
        .write_u8(0x05);
    a.send(&server, cs::CSChangeLootingRulePacket, s).await;
    let rule = server
        .systems
        .social
        .teams
        .active_team(team_id)
        .unwrap()
        .looting_rule;
    // Flags 1 | 4: method and loot master (kept: the method is LootMaster); grade unchanged.
    assert_eq!(
        (rule.loot_method, rule.minimum_grade, rule.loot_master),
        (2, 2, 2)
    );
    assert_eq!(b.ops(), vec![sc::SCTeamLootingRuleChangedPacket]);
    a.ops();

    // B goes offline: A sees the disconnect (obj id 0 in WritePerson); the team stays.
    crate::world::session::set_online(&server, &b.p, false);
    let fa = a.frames();
    assert_eq!(fa.len(), 1);
    assert_eq!(fa[0].0, sc::SCTeamMemberDisconnectedPacket);
    assert_eq!(&fa[0].1[8 + 38..8 + 41], &[0, 0, 0]);
    assert!(server.systems.social.teams.active_team(team_id).is_some());

    // B reconnects (UpdateAtLogin): full team + the other members' snapshots; A sees the
    // member rejoin, its snapshot and the forced ObjId refresh. (B never left its chat
    // channels: only LeaveWorldTask leaves them, so the re-joins send nothing.)
    crate::world::session::set_online(&server, &b.p, true);
    server.systems.social.teams.update_at_login(&server, &b.p);
    assert_eq!(
        b.ops(),
        vec![
            sc::SCJoinedTeamPacket,
            sc::SCTeamRemoteMembersExPacket,
            sc::SCRefreshTeamMemberPacket,
        ]
    );
    assert_eq!(
        a.ops(),
        vec![
            sc::SCTeamMemberJoinedPacket,
            sc::SCTeamRemoteMembersExPacket,
            sc::SCRefreshTeamMemberPacket
        ]
    );

    // Everybody offline -> the zombie team is disbanded after the grace period.
    crate::world::session::set_online(&server, &a.p, false);
    crate::world::session::set_online(&server, &b.p, false);
    let now = crate::systems::social::util::now_ms();
    server.systems.social.teams.update_all_teams(&server, now);
    assert!(server.systems.social.teams.active_team(team_id).is_some());
    server.systems.social.teams.update_all_teams(
        &server,
        now + crate::systems::social::team::OFFLINE_TEAM_DISBAND_GRACE_MS,
    );
    assert!(server.systems.social.teams.active_team(team_id).is_none());
    assert!(!a.p.data().in_party && !b.p.data().in_party);
}

#[tokio::test]
async fn remote_member_sync_is_delta_cached() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 1900.0, 1900.0);
    settle(&mut [&mut a, &mut b]);
    let team_id = form_party(&server, &mut a, &mut b).await;
    a.ops();
    b.ops();
    let teams = &server.systems.social.teams;
    teams.update_all_teams(&server, 1_000);
    assert_eq!(a.ops(), vec![sc::SCTeamRemoteMembersExPacket]);
    assert_eq!(b.ops(), vec![sc::SCTeamRemoteMembersExPacket]);
    // Nothing changed: no traffic until the forced resync (5 s).
    teams.update_all_teams(&server, 1_500);
    assert!(a.ops().is_empty() && b.ops().is_empty());
    b.p.data().unit.hp -= 10;
    teams.update_all_teams(&server, 2_000);
    assert_eq!(a.ops(), vec![sc::SCTeamRemoteMembersExPacket]);
    assert!(b.ops().is_empty());
    // Forced resync 5 s after each direction's last send.
    teams.update_all_teams(&server, 6_000);
    assert!(a.ops().is_empty());
    assert_eq!(b.ops(), vec![sc::SCTeamRemoteMembersExPacket]);
    teams.update_all_teams(&server, 7_000);
    assert_eq!(a.ops(), vec![sc::SCTeamRemoteMembersExPacket]);
    assert!(b.ops().is_empty());
    // In render distance -> no remote sync.
    b.p.data().unit.transform.set_xyz(120.0, 100.0, 100.0);
    let world = server.world.world(0).unwrap();
    world.add_visible_object(&b.p, &server.view_ctx());
    a.ops();
    b.ops();
    teams.update_all_teams(&server, 20_000);
    assert!(a.ops().is_empty() && b.ops().is_empty());
    let _ = team_id;
}

/// Five members in a party, A has the money: the guild is created for everybody.
#[tokio::test]
async fn guild_create_invite_kick_roles_and_disband() {
    let server = test_server();
    let mut pcs: Vec<Pc> = (1..=5)
        .map(|i| pc(&server, i, &format!("M{i}"), 100.0 + i as f32, 100.0))
        .collect();
    let mut outsider = pc(&server, 9, "Out", 120.0, 100.0);
    for x in pcs.iter_mut() {
        x.c.drain();
    }
    outsider.c.drain();
    {
        let teams = &server.systems.social.teams;
        for i in 1..5 {
            let name = format!("M{}", i + 1);
            let (lead, rest) = pcs.split_at_mut(i);
            teams.ask_to_join(&server, &lead[0].p, &name, 0, true, None);
            teams.reply_to_join_team(&server, &rest[0].p, 0, true, 1, false, "", false);
        }
        assert_eq!(teams.active_team_by_unit(1).unwrap().members_count(), 5);
    }
    pcs[0].p.data().money = 20_000;
    for x in pcs.iter_mut() {
        x.ops();
    }
    outsider.ops();

    // Bad names first.
    let create = |name: &str| {
        let mut s = PacketStream::new();
        s.write_str(name).write_u32(0);
        s
    };
    pcs[0]
        .send(&server, cs::CSCreateExpeditionPacket, create("x1"))
        .await;
    assert_eq!(pcs[0].ops(), vec![sc::SCErrorMsgPacket]);

    pcs[0]
        .send(&server, cs::CSCreateExpeditionPacket, create("Knights"))
        .await;
    let id = pcs[0].p.data().unit.expedition_id;
    assert_ne!(id, 0);
    assert_eq!(pcs[0].p.data().money, 10_000);
    let owner_ops = pcs[0].ops();
    assert_eq!(owner_ops[0], sc::SCItemTaskSuccessPacket);
    assert_eq!(owner_ops[1], sc::SCFactionCreatedPacket);
    assert_eq!(owner_ops[2], sc::SCFactionListPacket);
    assert!(owner_ops.contains(&sc::SCUnitExpeditionChangedPacket));
    assert!(owner_ops.contains(&sc::SCExpeditionMemberListPacket));
    assert!(owner_ops.contains(&sc::SCExpeditionMemberStatusChangedPacket));
    let e = server.expeditions.get(id).unwrap();
    assert_eq!(e.members.len(), 5);
    assert_eq!(e.members[0].role, 255);
    assert_eq!(e.policies.len(), 5);
    for x in pcs.iter_mut().skip(1) {
        assert_eq!(x.p.data().unit.expedition_id, id);
        let ops = x.ops();
        assert!(ops.contains(&sc::SCExpeditionRolePolicyListPacket));
        assert!(ops.contains(&sc::SCJoinedChatChannelPacket));
    }
    // Everybody on the server learns the new faction.
    assert!(outsider.ops().contains(&sc::SCFactionListPacket));

    // Guild chat.
    pcs[1]
        .send(
            &server,
            cs::CSSendChatMessagePacket,
            chat(chat_type::CLAN, "gg"),
        )
        .await;
    for x in pcs.iter_mut() {
        assert_eq!(x.ops(), vec![sc::SCChatMessagePacket]);
    }

    // Invite the outsider (owner has the Invite policy) and accept.
    let mut s = PacketStream::new();
    s.write_str("Out");
    pcs[0]
        .send(&server, cs::CSInviteToExpeditionPacket, s)
        .await;
    let inv = find(&outsider.frames(), sc::SCExpeditionInvitationPacket);
    assert_eq!(inv.len(), 1);
    let mut s = PacketStream::new();
    s.write_u32(id).write_u32(1).write_bool(true);
    outsider
        .send(&server, cs::CSReplyExpeditionInvitationPacket, s)
        .await;
    assert_eq!(outsider.p.data().unit.expedition_id, id);
    assert_eq!(server.expeditions.get(id).unwrap().members.len(), 6);
    for x in pcs.iter_mut() {
        x.ops();
    }
    outsider.ops();

    // A member (role 0) cannot kick; the owner can.
    let mut s = PacketStream::new();
    s.write_u32(9);
    pcs[1]
        .send(&server, cs::CSKickFromExpeditionPacket, s)
        .await;
    assert_eq!(server.expeditions.get(id).unwrap().members.len(), 6);
    let mut s = PacketStream::new();
    s.write_u32(9);
    pcs[0]
        .send(&server, cs::CSKickFromExpeditionPacket, s)
        .await;
    assert_eq!(outsider.p.data().unit.expedition_id, 0);
    let fo = outsider.frames();
    assert_eq!(fo[0].0, sc::SCLeavedChatChannelPacket);
    assert_eq!(fo[1].0, sc::SCUnitExpeditionChangedPacket);
    let mut s = PacketStream::from_bytes(fo[1].1.clone());
    s.read_bc().unwrap();
    assert_eq!(s.read_u32().unwrap(), 9);
    assert_eq!(s.read_string().unwrap(), "M1");
    assert_eq!(s.read_string().unwrap(), "Out");
    assert_eq!(s.read_u32().unwrap(), id);
    assert_eq!(s.read_u32().unwrap(), 0);
    assert!(s.read_bool().unwrap());
    for x in pcs.iter_mut() {
        x.ops();
    }

    // Promote M2 to officer (role 2), then hand the guild over to M2.
    let mut s = PacketStream::new();
    s.write_u8(2).write_u32(2);
    pcs[0]
        .send(&server, cs::CSChangeExpeditionMemberRolePacket, s)
        .await;
    assert_eq!(
        server.expeditions.get(id).unwrap().member(2).unwrap().role,
        2
    );
    for x in pcs.iter_mut() {
        assert_eq!(x.ops(), vec![sc::SCExpeditionRoleChangedPacket]);
    }
    let mut s = PacketStream::new();
    s.write_u32(2);
    pcs[0]
        .send(&server, cs::CSChangeExpeditionOwnerPacket, s)
        .await;
    let e = server.expeditions.get(id).unwrap();
    assert_eq!(e.faction.owner_id, 2);
    assert_eq!(
        (e.member(1).unwrap().role, e.member(2).unwrap().role),
        (0, 255)
    );
    for x in pcs.iter_mut() {
        assert_eq!(
            x.ops(),
            vec![
                sc::SCExpeditionOwnerChangedPacket,
                sc::SCExpeditionRoleChangedPacket,
                sc::SCExpeditionRoleChangedPacket
            ]
        );
    }

    // M3 leaves.
    pcs[2]
        .send(&server, cs::CSLeaveExpeditionPacket, PacketStream::new())
        .await;
    assert_eq!(pcs[2].p.data().unit.expedition_id, 0);
    assert_eq!(server.expeditions.get(id).unwrap().members.len(), 4);
    for x in pcs.iter_mut() {
        x.ops();
    }

    // Only the owner can disband.
    pcs[0]
        .send(&server, cs::CSDismissExpeditionPacket, PacketStream::new())
        .await;
    assert_eq!(pcs[0].ops(), vec![sc::SCErrorMsgPacket]);
    pcs[1]
        .send(&server, cs::CSDismissExpeditionPacket, PacketStream::new())
        .await;
    let e = server.expeditions.get(id).unwrap();
    assert!(e.is_disbanded && e.members.is_empty());
    assert_eq!(e.faction.name, format!("$deleted-guild-{id}"));
    assert_eq!(
        pcs[1].ops(),
        vec![
            sc::SCExpeditionDismissedPacket,
            sc::SCLeavedChatChannelPacket
        ]
    );
    assert_eq!(pcs[0].p.data().unit.expedition_id, 0);
}

#[tokio::test]
async fn guild_creation_needs_a_full_party() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 110.0, 100.0);
    settle(&mut [&mut a, &mut b]);
    let mut s = PacketStream::new();
    s.write_str("Knights").write_u32(0);
    a.send(&server, cs::CSCreateExpeditionPacket, s.clone())
        .await;
    assert_eq!(a.ops(), vec![sc::SCErrorMsgPacket]); // no party
    form_party(&server, &mut a, &mut b).await;
    a.ops();
    a.send(&server, cs::CSCreateExpeditionPacket, s).await;
    let f = a.frames();
    assert_eq!(f.len(), 1);
    assert_eq!(f[0].1[..2], 294i16.to_le_bytes()); // ExpeditionCreateMember
}

#[tokio::test]
async fn family_create_join_leave_disband() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 110.0, 100.0);
    let mut c = pc(&server, 3, "Cy", 120.0, 100.0);
    settle(&mut [&mut a, &mut b, &mut c]);

    let invite = |name: &str| {
        let mut s = PacketStream::new();
        s.write_str(name).write_str("Kid");
        s
    };
    let reply = |invitor: u32| {
        let mut s = PacketStream::new();
        s.write_u32(invitor).write_bool(true).write_str("Kid");
        s
    };
    a.send(&server, cs::CSFamilyInviteMemberPacket, invite("Bob"))
        .await;
    assert_eq!(b.ops(), vec![sc::SCFamilyInvitationPacket]);
    b.send(&server, cs::CSFamilyReplyInvitationPacket, reply(1))
        .await;
    let fid = a.p.data().family;
    assert_ne!(fid, 0);
    assert_eq!(b.p.data().family, fid);
    // Both join the family chat; the created packet goes to both members.
    assert_eq!(
        a.ops(),
        vec![sc::SCJoinedChatChannelPacket, sc::SCFamilyCreatedPacket]
    );
    assert_eq!(
        b.ops(),
        vec![sc::SCJoinedChatChannelPacket, sc::SCFamilyCreatedPacket]
    );
    let f = server.systems.social.families.family(fid).unwrap();
    assert_eq!(
        f.info().members,
        vec![
            (1, "Al".into(), 1, true, "".into()),
            (2, "Bob".into(), 0, true, "Kid".into())
        ]
    );

    // Family chat.
    b.send(
        &server,
        cs::CSSendChatMessagePacket,
        chat(chat_type::FAMILY, "hey"),
    )
    .await;
    assert_eq!(a.ops(), vec![sc::SCChatMessagePacket]);
    assert_eq!(b.ops(), vec![sc::SCChatMessagePacket]);

    // C joins the existing family.
    a.send(&server, cs::CSFamilyInviteMemberPacket, invite("Cy"))
        .await;
    c.ops();
    c.send(&server, cs::CSFamilyReplyInvitationPacket, reply(1))
        .await;
    assert_eq!(c.p.data().family, fid);
    assert_eq!(a.ops(), vec![sc::SCFamilyMemberAddedPacket]);
    assert_eq!(
        c.ops(),
        vec![sc::SCJoinedChatChannelPacket, sc::SCFamilyMemberAddedPacket]
    );
    b.ops();

    // Title / owner changes by the steward.
    let mut s = PacketStream::new();
    s.write_u32(3).write_str("Baby");
    a.send(&server, cs::CSFamilyChangeTitlePacket, s).await;
    assert_eq!(b.ops(), vec![sc::SCFamilyTitleChangedPacket]);
    let mut s = PacketStream::new();
    s.write_u32(2);
    a.send(&server, cs::CSFamilyChangeOwnerPacket, s).await;
    assert_eq!(
        c.ops(),
        vec![
            sc::SCFamilyTitleChangedPacket,
            sc::SCFamilyOwnerChangedPacket,
            sc::SCFamilyDescPacket
        ]
    );
    a.ops();
    b.ops();

    // Logout / login status.
    crate::systems::social::family::on_character_logout(&server, &c.p);
    assert_eq!(
        a.frames(),
        vec![(
            sc::SCFamilyMemberOnlinePacket,
            body(
                &crate::systems::social::packets::family::SCFamilyMemberOnlinePacket {
                    family_id: fid,
                    member_id: 3,
                    online: false
                }
            )
        )]
    );
    assert_eq!(c.ops(), vec![sc::SCLeavedChatChannelPacket]);
    crate::systems::social::family::on_character_login(&server, &c.p);
    assert_eq!(
        c.ops(),
        vec![
            sc::SCJoinedChatChannelPacket,
            sc::SCFamilyDescPacket,
            sc::SCFamilyMemberOnlinePacket
        ]
    );
    a.ops();
    b.ops();

    // B (now steward) kicks C; then A leaves -> fewer than two members: disbanded.
    let mut s = PacketStream::new();
    s.write_u32(3);
    b.send(&server, cs::CSFamilyKickPacket, s).await;
    assert_eq!(c.p.data().family, 0);
    assert_eq!(
        c.ops(),
        vec![sc::SCLeavedChatChannelPacket, sc::SCFamilyRemovedPacket]
    );
    assert_eq!(a.ops(), vec![sc::SCFamilyMemberRemovedPacket]);
    b.ops();
    a.send(&server, cs::CSFamilyLeavePacket, PacketStream::new())
        .await;
    assert_eq!(
        a.ops(),
        vec![sc::SCFamilyRemovedPacket, sc::SCLeavedChatChannelPacket]
    );
    assert_eq!(
        b.ops(),
        vec![
            sc::SCFamilyMemberRemovedPacket,
            sc::SCLeavedChatChannelPacket,
            sc::SCFamilyRemovedPacket
        ]
    );
    assert_eq!((a.p.data().family, b.p.data().family), (0, 0));
    assert!(server.systems.social.families.family(fid).is_none());
}

#[tokio::test]
async fn friends_and_blocked_users() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 110.0, 100.0);
    settle(&mut [&mut a, &mut b]);
    let name = |n: &str| {
        let mut s = PacketStream::new();
        s.write_str(n);
        s
    };
    a.send(&server, cs::CSAddFriendPacket, name("Bob")).await;
    let f = a.frames();
    assert_eq!(f.len(), 1);
    assert_eq!(f[0].0, sc::SCAddFriendPacket);
    assert_eq!(a.p.data().friends.len(), 1);
    assert_eq!(server.friends.owners_of(2), vec![1]);
    // Adding twice does nothing.
    a.send(&server, cs::CSAddFriendPacket, name("Bob")).await;
    assert!(a.ops().is_empty());
    // B's status changes reach A.
    crate::world::session::set_online(&server, &b.p, false);
    assert_eq!(a.ops(), vec![sc::SCFriendStatusChangedPacket]);
    crate::world::session::set_online(&server, &b.p, true);
    a.ops();
    a.send(&server, cs::CSDeleteFriendPacket, name("Bob")).await;
    assert_eq!(a.ops(), vec![sc::SCDeleteFriendPacket]);
    assert!(a.p.data().friends.is_empty());
    assert_eq!(a.p.data().removed_friends, vec![2]);
    assert!(server.friends.owners_of(2).is_empty());

    a.send(&server, cs::CSAddBlockedUserPacket, name("bob"))
        .await;
    assert_eq!(
        a.frames(),
        vec![(
            sc::SCAddBlockedUserPacket,
            body(
                &crate::systems::social::packets::friend::SCAddBlockedUserPacket {
                    character_id: 2,
                    character_name: "Bob".into(),
                    success: true,
                    error_message: 0
                }
            )
        )]
    );
    a.send(&server, cs::CSDeleteBlockedUserPacket, name("Bob"))
        .await;
    assert_eq!(a.ops(), vec![sc::SCDeleteBlockedUserPacket]);
    assert_eq!(a.p.data().removed_blocked, vec![2]);
    // Unknown (offline) names are ignored for blocks.
    a.send(&server, cs::CSAddBlockedUserPacket, name("Nobody"))
        .await;
    assert!(a.ops().is_empty());
    let _ = b.ops();
}

#[tokio::test]
async fn invite_area_skips_party_members_and_hostiles() {
    let server = test_server();
    let mut a = pc(&server, 1, "Al", 100.0, 100.0);
    let mut b = pc(&server, 2, "Bob", 150.0, 100.0);
    let mut far = pc(&server, 3, "Far", 300.0, 100.0);
    settle(&mut [&mut a, &mut b, &mut far]);
    let mut s = PacketStream::new();
    s.write_u32(0).write_bool(true);
    a.send(&server, cs::CSInviteAreaToTeamPacket, s).await;
    assert_eq!(b.ops(), vec![sc::SCAskToJoinTeamPacket]);
    // 200 m away: outside the 100 m radius.
    assert!(far.ops().is_empty());
    let _ = a.ops();
}
