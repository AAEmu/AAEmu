//! Stream server client (C2S `CT*` / S2C `TC*`): `[u16 len][u16 opcode][body]`, no level byte.

use std::time::Duration;

use aaemu_commons::net::FrameCodec;
use aaemu_commons::PacketStream;
use anyhow::{anyhow, bail, Context};
use futures::{SinkExt, StreamExt};
use tokio::net::TcpStream;
use tokio_util::codec::Framed;

use crate::record::{Channel, Dir, Recorder};

pub const CT_JOIN: u16 = 0x001;
pub const TC_JOIN_RESPONSE: u16 = 0x001;

pub fn tc_name(op: u16) -> &'static str {
    match op {
        0x001 => "TCJoinResponsePacket",
        0x002 => "TCDoodadStreamPacket",
        0x003 => "TCDoodadIdsPacket",
        0x004 => "TCDownloadEmblemPacket",
        0x005 => "TCUccComplexPacket",
        0x006 => "TCUccStringPacket",
        0x007 => "TCUccPositionPacket",
        0x008 => "TCUccCharNamePacket",
        0x009 => "TCCharNameQueriedPacket",
        0x00d => "TCItemUccDataPacket",
        0x00e => "TCHouseFarmPacket",
        _ => "TCUnknown",
    }
}

pub struct StreamClient {
    framed: Framed<TcpStream, FrameCodec>,
    rec: Recorder,
    pub timeout: Duration,
}

impl StreamClient {
    pub async fn connect(addr: &str, rec: Recorder) -> anyhow::Result<Self> {
        let sock = TcpStream::connect(addr)
            .await
            .with_context(|| format!("connecting to stream server {addr}"))?;
        sock.set_nodelay(true)?;
        Ok(Self {
            framed: Framed::new(sock, FrameCodec),
            rec,
            timeout: Duration::from_secs(10),
        })
    }

    pub async fn send(&mut self, opcode: u16, name: &str, body: &[u8]) -> anyhow::Result<()> {
        self.rec
            .push(Channel::Stream, Dir::C2S, 0, opcode, name, body);
        let mut p = opcode.to_le_bytes().to_vec();
        p.extend_from_slice(body);
        self.framed.send(p).await?;
        Ok(())
    }

    pub async fn recv(&mut self) -> anyhow::Result<(u16, PacketStream)> {
        let frame = tokio::time::timeout(self.timeout, self.framed.next())
            .await
            .map_err(|_| anyhow!("timed out waiting for a stream packet"))?
            .ok_or_else(|| anyhow!("stream server closed the connection"))??;
        if frame.len() < 2 {
            bail!("short stream frame");
        }
        let op = u16::from_le_bytes([frame[0], frame[1]]);
        self.rec
            .push(Channel::Stream, Dir::S2C, 0, op, tc_name(op), &frame[2..]);
        let mut s = PacketStream::from_bytes(frame);
        s.set_pos(2);
        Ok((op, s))
    }

    /// `CTJoinPacket` -> `TCJoinResponsePacket`; returns the response code (0 = accepted).
    pub async fn join(&mut self, account_id: u32, token: u32) -> anyhow::Result<u8> {
        let mut s = PacketStream::new();
        s.write_u32(account_id).write_u32(token);
        self.send(CT_JOIN, "CTJoinPacket", s.as_bytes()).await?;
        let (op, mut r) = self.recv().await?;
        if op != TC_JOIN_RESPONSE {
            bail!("expected TCJoinResponsePacket, got {}", tc_name(op));
        }
        Ok(r.read_u8()?)
    }
}
