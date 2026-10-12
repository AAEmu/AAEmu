//! `aaemu-clientdriver` — controller side of the ArcheAge 1.2 client Lua driver.
//!
//! The in-client part is `native/aadriver.cpp` (a 32-bit DLL, built with MSVC because the
//! client is x86). Once it is loaded into `archeage.exe` it serves the named pipe
//! `\\.\pipe\aaemu-clientdriver-<pid>` and runs Lua chunks on the client's main thread in one of
//! the engine's Lua states (see `docs/client/LUA-DRIVER.md`).
//!
//! Wire format, both directions: `u32 LE length` + `length` bytes.
//! * request: one op byte + payload (`S`/`U`/`T` + Lua = run in the System / UI / third state,
//!   `B` + target + Lua = the engine's own `ExecuteBuffer`, `P` = ping, `X` = detach,
//!   `C` = capture, `Whide`/`Wshow` = window visibility, `F` + action = pak I/O);
//! * response: one status byte (`0` ok, `1` Lua error, `2` driver error) + UTF-8 text,
//!   except successful capture replies, which contain binary PNG bytes.
//!
//! This module holds the platform independent pieces (framing, the PE export lookup used to
//! find the 32-bit `LoadLibraryW`); [`win`] does the Win32 part (pipe, process lookup, DLL
//! injection).

use std::io::{Read, Write};

/// Named pipe of the driver inside the client with process id `pid`.
pub fn pipe_name(pid: u32) -> String {
    format!(r"\\.\pipe\aaemu-clientdriver-{pid}")
}

/// Which Lua state a chunk runs in.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Target {
    /// `gEnv->pScriptSystem` (`lua_handle 0`): entities, `System.*`, `Script.*`, ...
    System,
    /// The X2 UI script system (`lua_handle 1`): the game UI (login, lobby, HUD).
    Ui,
    /// The third script system slot (`lua_handle 2`), if the client created one.
    Third,
}

impl Target {
    pub fn op(self) -> u8 {
        match self {
            Target::System => b'S',
            Target::Ui => b'U',
            Target::Third => b'T',
        }
    }
}

/// A request to the driver.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Request {
    /// Compile `code` as `return <code>` (else as statements), run it, return its values.
    Lua(Target, String),
    /// Run `code` through `IScriptSystem::ExecuteBuffer` (no values, errors go to the log).
    ExecuteBuffer(Target, String),
    Ping,
    /// Captures the next DXGI back buffer. The reply contains binary PNG bytes.
    Screenshot,
    /// Shows or hides the window without minimising the renderer.
    Window(bool),
    /// Controls pak read-ahead, raw I/O profiling, or engine read byte verification.
    PakIo(String),
    /// Posts bounded keyboard events to CryInput; release runs in the client even after disconnect.
    Input(String),
    /// Controls bounded DXGI frame interval recording.
    FrameProfile(String),
    /// Detaches hooks; the native module stays mapped until the client exits.
    Unload,
}

impl Request {
    /// The request payload (without the length prefix).
    pub fn payload(&self) -> Vec<u8> {
        match self {
            Request::Lua(t, code) => [&[t.op()][..], code.as_bytes()].concat(),
            Request::ExecuteBuffer(t, code) => [&[b'B', t.op()][..], code.as_bytes()].concat(),
            Request::Ping => vec![b'P'],
            Request::Screenshot => vec![b'C'],
            Request::Window(visible) => {
                if *visible {
                    b"Wshow".to_vec()
                } else {
                    b"Whide".to_vec()
                }
            }
            Request::Unload => vec![b'X'],
            Request::PakIo(action) => [b"F".as_slice(), action.as_bytes()].concat(),
            Request::Input(action) => [b"I".as_slice(), action.as_bytes()].concat(),
            Request::FrameProfile(action) => [b"V".as_slice(), action.as_bytes()].concat(),
        }
    }
}

/// Validates a binary screenshot reply without converting PNG data to UTF-8.
pub fn screenshot_reply(bytes: &[u8]) -> anyhow::Result<Vec<u8>> {
    match bytes.split_first() {
        Some((b'0', png)) if png.starts_with(b"\x89PNG\r\n\x1a\n") => Ok(png.to_vec()),
        Some((b'0', _)) => anyhow::bail!("driver returned an invalid PNG"),
        _ => Reply::parse(bytes).into_result().map(|_| Vec::new()),
    }
}

/// The driver's answer.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Reply {
    Ok(String),
    LuaError(String),
    DriverError(String),
}

impl Reply {
    pub fn parse(bytes: &[u8]) -> Reply {
        let text = |b: &[u8]| String::from_utf8_lossy(b).into_owned();
        match bytes.split_first() {
            Some((b'0', rest)) => Reply::Ok(text(rest)),
            Some((b'1', rest)) => Reply::LuaError(text(rest)),
            Some((_, rest)) => Reply::DriverError(text(rest)),
            None => Reply::DriverError("empty reply".into()),
        }
    }

    pub fn into_result(self) -> anyhow::Result<String> {
        match self {
            Reply::Ok(s) => Ok(s),
            Reply::LuaError(e) => Err(anyhow::anyhow!("lua error: {e}")),
            Reply::DriverError(e) => Err(anyhow::anyhow!("driver error: {e}")),
        }
    }
}

/// Writes one length-prefixed frame.
pub fn write_frame<W: Write>(w: &mut W, payload: &[u8]) -> std::io::Result<()> {
    let len = u32::try_from(payload.len())
        .map_err(|_| std::io::Error::new(std::io::ErrorKind::InvalidInput, "frame too large"))?;
    w.write_all(&len.to_le_bytes())?;
    w.write_all(payload)?;
    w.flush()
}

/// Reads one length-prefixed frame.
pub fn read_frame<R: Read>(r: &mut R) -> std::io::Result<Vec<u8>> {
    let mut len = [0u8; 4];
    r.read_exact(&mut len)?;
    let len = u32::from_le_bytes(len) as usize;
    if len > 64 << 20 {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidData,
            "frame too large",
        ));
    }
    let mut buf = vec![0u8; len];
    r.read_exact(&mut buf)?;
    Ok(buf)
}

/// One request / reply round trip over any byte stream.
pub fn roundtrip<S: Read + Write>(s: &mut S, req: &Request) -> std::io::Result<Reply> {
    write_frame(s, &req.payload())?;
    Ok(Reply::parse(&read_frame(s)?))
}

/// Looks up the RVA of an exported function in a PE32 / PE32+ image file (on-disk layout).
/// Returns `Err` for forwarded exports (`"forwarded to X"`).
pub fn export_rva(image: &[u8], name: &str) -> anyhow::Result<u32> {
    use anyhow::{bail, Context};
    let u16_at = |o: usize| -> anyhow::Result<u16> {
        Ok(u16::from_le_bytes(
            image.get(o..o + 2).context("truncated PE")?.try_into()?,
        ))
    };
    let u32_at = |o: usize| -> anyhow::Result<u32> {
        Ok(u32::from_le_bytes(
            image.get(o..o + 4).context("truncated PE")?.try_into()?,
        ))
    };
    if u16_at(0)? != 0x5A4D {
        bail!("not an MZ image");
    }
    let nt = u32_at(0x3C)? as usize;
    if u32_at(nt)? != 0x4550 {
        bail!("not a PE image");
    }
    let n_sections = u16_at(nt + 6)? as usize;
    let opt_size = u16_at(nt + 20)? as usize;
    let opt = nt + 24;
    let data_dirs = match u16_at(opt)? {
        0x10B => opt + 96,  // PE32
        0x20B => opt + 112, // PE32+
        m => bail!("unknown optional header magic {m:#x}"),
    };
    let (exp_rva, exp_size) = (u32_at(data_dirs)?, u32_at(data_dirs + 4)?);
    if exp_rva == 0 {
        bail!("image has no exports");
    }
    let sections = opt + opt_size;
    let to_off = |rva: u32| -> anyhow::Result<usize> {
        for i in 0..n_sections {
            let s = sections + i * 40;
            let (vsize, va, raw_size, raw) = (
                u32_at(s + 8)?,
                u32_at(s + 12)?,
                u32_at(s + 16)?,
                u32_at(s + 20)?,
            );
            if rva >= va && rva < va + vsize.max(raw_size) {
                return Ok((rva - va + raw) as usize);
            }
        }
        bail!("rva {rva:#x} outside all sections")
    };
    let ed = to_off(exp_rva)?;
    let n_names = u32_at(ed + 24)? as usize;
    let functions = to_off(u32_at(ed + 28)?)?;
    let names = to_off(u32_at(ed + 32)?)?;
    let ordinals = to_off(u32_at(ed + 36)?)?;
    for i in 0..n_names {
        let name_off = to_off(u32_at(names + 4 * i)?)?;
        let end = image[name_off..]
            .iter()
            .position(|&b| b == 0)
            .context("unterminated export name")?;
        if &image[name_off..name_off + end] == name.as_bytes() {
            let ord = u16_at(ordinals + 2 * i)? as usize;
            let rva = u32_at(functions + 4 * ord)?;
            if rva >= exp_rva && rva < exp_rva + exp_size {
                let f = to_off(rva)?;
                let e = image[f..].iter().position(|&b| b == 0).unwrap_or(0);
                bail!(
                    "{name} is forwarded to {}",
                    String::from_utf8_lossy(&image[f..f + e])
                );
            }
            return Ok(rva);
        }
    }
    bail!("export {name} not found")
}

pub mod mcp;
#[cfg(windows)]
pub mod win;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn payloads() {
        assert_eq!(
            Request::Lua(Target::Ui, "1+1".into()).payload(),
            b"U1+1".to_vec()
        );
        assert_eq!(
            Request::ExecuteBuffer(Target::System, "x=1".into()).payload(),
            b"BSx=1".to_vec()
        );
        assert_eq!(Request::Ping.payload(), b"P".to_vec());
        assert_eq!(Request::Screenshot.payload(), b"C".to_vec());
        assert_eq!(Request::Window(false).payload(), b"Whide".to_vec());
        assert_eq!(Request::Unload.payload(), b"X".to_vec());
        assert_eq!(Request::PakIo("profile".into()).payload(), b"Fprofile");
        assert_eq!(Request::Input("w 1000".into()).payload(), b"Iw 1000");
        assert_eq!(Request::FrameProfile("off".into()).payload(), b"Voff");
    }

    #[test]
    fn replies() {
        assert_eq!(Reply::parse(b"0ok"), Reply::Ok("ok".into()));
        assert_eq!(Reply::parse(b"1bad"), Reply::LuaError("bad".into()));
        assert_eq!(Reply::parse(b"2x"), Reply::DriverError("x".into()));
        assert_eq!(Reply::parse(b""), Reply::DriverError("empty reply".into()));
        assert!(Reply::parse(b"1e").into_result().is_err());
    }

    #[test]
    fn frames_round_trip() {
        let mut buf = Vec::new();
        write_frame(&mut buf, b"hello").unwrap();
        assert_eq!(&buf[..4], &5u32.to_le_bytes());
        let mut r = &buf[..];
        assert_eq!(read_frame(&mut r).unwrap(), b"hello");
    }

    #[test]
    fn screenshot_is_binary_and_errors_are_preserved() {
        let png = b"\x89PNG\r\n\x1a\n\xff\0";
        assert_eq!(
            screenshot_reply(&[b"0".as_slice(), png].concat()).unwrap(),
            png
        );
        assert!(screenshot_reply(b"0not PNG").is_err());
        assert!(screenshot_reply(b"2no frame")
            .unwrap_err()
            .to_string()
            .contains("no frame"));
    }

    #[test]
    fn export_lookup_on_own_kernel32() {
        // Any Windows box: the 64-bit kernel32 exports LoadLibraryW directly.
        #[cfg(windows)]
        {
            let dir = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".into());
            for sub in ["System32", "SysWOW64"] {
                let p = std::path::Path::new(&dir).join(sub).join("kernel32.dll");
                if let Ok(img) = std::fs::read(&p) {
                    let rva = export_rva(&img, "LoadLibraryW").unwrap();
                    assert!(rva > 0x1000, "{}: {rva:#x}", p.display());
                    assert!(export_rva(&img, "NoSuchExport___").is_err());
                }
            }
        }
        assert!(export_rva(b"not a pe", "x").is_err());
    }
}
