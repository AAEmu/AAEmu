//! Login server client (C2L / L2C), as spoken by the 1.2 (r208022) Trion client.
//!
//! Frames are `[u16 len][u16 opcode][body]`. The 1.2 client started by a launcher authenticates
//! with `CARequestAuthTrionPacket`: an XML ticket carrying the user name and the hex SHA-256 of
//! the password.

use std::net::Ipv4Addr;
use std::time::Duration;

use aaemu_commons::net::FrameCodec;
use aaemu_commons::PacketStream;
use anyhow::{anyhow, bail, Context};
use futures::{SinkExt, StreamExt};
use sha2::{Digest, Sha256};
use tokio::net::TcpStream;
use tokio_util::codec::Framed;

use crate::record::{Channel, Dir, Recorder};

/// C2L opcodes.
pub mod ca {
    pub const REQUEST_AUTH_TRION: u16 = 0x004;
    pub const LIST_WORLD: u16 = 0x00b;
    pub const ENTER_WORLD: u16 = 0x00c;
    pub const CANCEL_ENTER_WORLD: u16 = 0x00d;
}

/// L2C opcodes.
pub mod ac {
    pub const JOIN_RESPONSE: u16 = 0x000;
    pub const CHALLENGE: u16 = 0x002;
    pub const AUTH_RESPONSE: u16 = 0x003;
    pub const CHALLENGE2: u16 = 0x004;
    pub const ENTER_OTP: u16 = 0x005;
    pub const SHOW_ARS: u16 = 0x006;
    pub const ENTER_PC_CERT: u16 = 0x007;
    pub const WORLD_LIST: u16 = 0x008;
    pub const WORLD_QUEUE: u16 = 0x009;
    pub const WORLD_COOKIE: u16 = 0x00a;
    pub const ENTER_WORLD_DENIED: u16 = 0x00b;
    pub const LOGIN_DENIED: u16 = 0x00c;
    pub const ACCOUNT_WARNED: u16 = 0x00d;
}

pub fn ac_name(op: u16) -> &'static str {
    match op {
        ac::JOIN_RESPONSE => "ACJoinResponsePacket",
        ac::CHALLENGE => "ACChallengePacket",
        ac::AUTH_RESPONSE => "ACAuthResponsePacket",
        ac::CHALLENGE2 => "ACChallenge2Packet",
        ac::ENTER_OTP => "ACEnterOtpPacket",
        ac::SHOW_ARS => "ACShowArsPacket",
        ac::ENTER_PC_CERT => "ACEnterPcCertPacket",
        ac::WORLD_LIST => "ACWorldListPacket",
        ac::WORLD_QUEUE => "ACWorldQueuePacket",
        ac::WORLD_COOKIE => "ACWorldCookiePacket",
        ac::ENTER_WORLD_DENIED => "ACEnterWorldDeniedPacket",
        ac::LOGIN_DENIED => "ACLoginDeniedPacket",
        ac::ACCOUNT_WARNED => "ACAccountWarnedPacket",
        _ => "ACUnknown",
    }
}

pub fn ca_name(op: u16) -> &'static str {
    match op {
        ca::REQUEST_AUTH_TRION => "CARequestAuthTrionPacket",
        ca::LIST_WORLD => "CAListWorldPacket",
        ca::ENTER_WORLD => "CAEnterWorldPacket",
        ca::CANCEL_ENTER_WORLD => "CACancelEnterWorldPacket",
        _ => "CAUnknown",
    }
}

/// A world of `ACWorldListPacket`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct World {
    pub id: u8,
    pub name: String,
    pub active: bool,
}

/// A character of `ACWorldListPacket`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LoginCharacter {
    pub account_id: u32,
    pub gs_id: u8,
    pub id: u32,
    pub name: String,
}

#[derive(Clone, Debug, Default)]
pub struct WorldList {
    pub worlds: Vec<World>,
    pub characters: Vec<LoginCharacter>,
}

/// `ACWorldCookiePacket`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct WorldCookie {
    pub cookie: u32,
    pub ip: Ipv4Addr,
    pub port: u16,
}

pub struct LoginClient {
    framed: Framed<TcpStream, FrameCodec>,
    rec: Recorder,
    pub timeout: Duration,
    pub account_id: u32,
}

/// The launcher's Trion ticket for `user` / `password`.
pub fn trion_ticket(user: &str, password: &str) -> String {
    let hash = hex::encode(Sha256::digest(password.as_bytes()));
    format!(
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\
         <authTicket version=\"1.2\"><storeToken>1</storeToken>\
         <username>{user}</username><password>{hash}</password></authTicket>"
    )
}

impl LoginClient {
    pub async fn connect(addr: &str, rec: Recorder) -> anyhow::Result<Self> {
        let sock = TcpStream::connect(addr)
            .await
            .with_context(|| format!("connecting to login server {addr}"))?;
        sock.set_nodelay(true)?;
        Ok(Self {
            framed: Framed::new(sock, FrameCodec),
            rec,
            timeout: Duration::from_secs(15),
            account_id: 0,
        })
    }

    pub async fn send(&mut self, opcode: u16, body: &[u8]) -> anyhow::Result<()> {
        self.rec
            .push(Channel::Login, Dir::C2S, 0, opcode, ca_name(opcode), body);
        let mut payload = Vec::with_capacity(body.len() + 2);
        payload.extend_from_slice(&opcode.to_le_bytes());
        payload.extend_from_slice(body);
        self.framed.send(payload).await?;
        Ok(())
    }

    /// Next L2C packet `(opcode, body)`.
    pub async fn recv(&mut self) -> anyhow::Result<(u16, PacketStream)> {
        let frame = tokio::time::timeout(self.timeout, self.framed.next())
            .await
            .map_err(|_| anyhow!("timed out waiting for a login packet"))?
            .ok_or_else(|| anyhow!("login server closed the connection"))??;
        if frame.len() < 2 {
            bail!("short login frame");
        }
        let opcode = u16::from_le_bytes([frame[0], frame[1]]);
        self.rec.push(
            Channel::Login,
            Dir::S2C,
            0,
            opcode,
            ac_name(opcode),
            &frame[2..],
        );
        let mut s = PacketStream::from_bytes(frame);
        s.set_pos(2);
        Ok((opcode, s))
    }

    async fn expect(&mut self, want: u16) -> anyhow::Result<PacketStream> {
        let (op, s) = self.recv().await?;
        if op == ac::LOGIN_DENIED {
            let mut s = s;
            let reason = s.read_u8().unwrap_or(0);
            bail!("login denied (reason {reason})");
        }
        if op != want {
            bail!(
                "expected {} but got {} (0x{op:03x})",
                ac_name(want),
                ac_name(op)
            );
        }
        Ok(s)
    }

    /// `CARequestAuthTrionPacket` -> `ACJoinResponsePacket` + `ACAuthResponsePacket`.
    pub async fn auth_trion(&mut self, user: &str, password: &str) -> anyhow::Result<u32> {
        let mut s = PacketStream::new();
        s.write_u32(0) // pFrom
            .write_u32(0) // pTo
            .write_bool(false) // dev
            .write_bytes_sized(&[0x00, 0x16, 0x3e, 0x00, 0x00, 0x01]) // mac
            .write_str(&trion_ticket(user, password))
            .write_str("") // signature
            .write_bool(true); // isLast
        self.send(ca::REQUEST_AUTH_TRION, s.as_bytes()).await?;
        let mut join = self.expect(ac::JOIN_RESPONSE).await?;
        let reason = join.read_u16()?;
        if reason != 0 {
            bail!("ACJoinResponse reason {reason}");
        }
        let mut auth = self.expect(ac::AUTH_RESPONSE).await?;
        self.account_id = auth.read_u32()?;
        Ok(self.account_id)
    }

    /// `CAListWorldPacket` -> `ACWorldListPacket`.
    pub async fn list_worlds(&mut self) -> anyhow::Result<WorldList> {
        let mut s = PacketStream::new();
        s.write_u64(0);
        self.send(ca::LIST_WORLD, s.as_bytes()).await?;
        let mut r = self.expect(ac::WORLD_LIST).await?;
        let mut out = WorldList::default();
        let n = r.read_u8()?;
        for _ in 0..n {
            let id = r.read_u8()?;
            let name = r.read_string()?;
            let active = r.read_bool()?;
            if active {
                r.read_bytes(10)?; // con + 9 race congestion values
            }
            out.worlds.push(World { id, name, active });
        }
        let n = r.read_u8()?;
        for _ in 0..n {
            let account_id = r.read_u32()?;
            let gs_id = r.read_u8()?;
            let id = r.read_u32()?;
            let name = r.read_string()?;
            r.read_bytes(2)?; // race, gender
            r.read_bytes_sized()?; // guid
            r.read_u64()?; // v
            out.characters.push(LoginCharacter {
                account_id,
                gs_id,
                id,
                name,
            });
        }
        Ok(out)
    }

    /// `CAEnterWorldPacket` -> `ACWorldCookiePacket`.
    pub async fn enter_world(&mut self, gs_id: u8) -> anyhow::Result<WorldCookie> {
        let mut s = PacketStream::new();
        s.write_u64(0).write_u8(gs_id);
        self.send(ca::ENTER_WORLD, s.as_bytes()).await?;
        let (op, mut r) = self.recv().await?;
        if op == ac::ENTER_WORLD_DENIED {
            let reason = r.read_u8().unwrap_or(0);
            bail!("enter world denied (reason {reason})");
        }
        if op != ac::WORLD_COOKIE {
            bail!("expected ACWorldCookiePacket, got {}", ac_name(op));
        }
        let cookie = r.read_u32()?;
        let ip = r.read_bytes(4)?;
        let port = r.read_u16()?;
        Ok(WorldCookie {
            cookie,
            // The address is written with its octets reversed.
            ip: Ipv4Addr::new(ip[3], ip[2], ip[1], ip[0]),
            port,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ticket_contains_hashed_password() {
        let t = trion_ticket("bob", "secret");
        assert!(t.contains("<username>bob</username>"));
        assert!(t.contains(
            "<password>2bb80d537b1da3e38bd30361aa855686bde0eacd7162fef6a25fe97bf527a25b</password>"
        ));
    }
}
