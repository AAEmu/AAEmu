//! `aaemu-launcher` — the launcher handoff of the ArcheAge 1.2 (Trion) client.
//!
//! The 1.2 client does not ask for a user name or password itself. A launcher authenticates it
//! by handing over a *ticket* through an inheritable, named Win32 file mapping:
//!
//! 1. ticket XML (what the client later sends to the login server in `CARequestAuthTrion`):
//!    `<?xml version="1.0" encoding="UTF - 8" standalone="yes"?><authTicket version = "1.2">`
//!    `<storeToken>1</storeToken><username>USER</username><password>HASH</password></authTicket>`
//!    where `HASH` is the lower-case hex SHA-256 of the password;
//! 2. plain text = `"TFIR" + signature + "\n" + xml` (signature `dGVzdA==`, base64 of `test`),
//!    encrypted with RC4 under a random 8-byte key;
//! 3. file mapping `archeage_auth_ticket_map` (page-file backed, read/write, inheritable, size
//!    = encrypted length + 12) holding `key[8] | u32 LE encrypted length | encrypted bytes`, plus
//!    an inheritable manual-reset event `archeage_auth_ticket_event` (initially unset);
//! 4. the client is started with the handles inherited and
//!    `-t +auth_ip HOST -auth_port PORT -handle MAP:EVENT [-lang LANG] [extra] +acpxmk`
//!    (handle values as 8 upper-case hex digits).
//!
//! This mirrors the `Trion12Launcher` of the public AAEmu Launcher
//! (<https://github.com/ZeromusXYZ/AAEmu-Launcher>). The byte layout lives in this platform
//! independent module (unit tested); [`win`] does the Win32 part.

use sha2::{Digest, Sha256};

/// Name of the file mapping holding the encrypted ticket.
pub const TICKET_MAP_NAME: &str = "archeage_auth_ticket_map";
/// Name of the event created next to the ticket mapping.
pub const TICKET_EVENT_NAME: &str = "archeage_auth_ticket_event";
/// Header of the plain-text ticket.
pub const TICKET_MAGIC: &str = "TFIR";
/// The (unchecked) signature the public launcher puts after the header: base64 of `test`.
pub const TICKET_SIGNATURE: &str = "dGVzdA==";
/// HackShield argument the public launcher always appends.
pub const HSHIELD_ARG: &str = "+acpxmk";

/// Lower-case hex SHA-256 of the password: what the login server stores / compares for Trion
/// logins (`AutoAccount` creates the account with it).
pub fn password_hash(password: &str) -> String {
    hex::encode(Sha256::digest(password.as_bytes()))
}

/// Escapes the XML special characters of a text node (the public launcher does not escape, so
/// for ordinary account names the result is byte-identical).
fn xml_text(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
}

/// The ticket XML exactly as the public launcher writes it (including its odd spacing).
pub fn ticket_xml(user: &str, password_hash: &str) -> String {
    format!(
        "<?xml version=\"1.0\" encoding=\"UTF - 8\" standalone=\"yes\"?>\
         <authTicket version = \"1.2\"><storeToken>1</storeToken>\
         <username>{}</username><password>{}</password></authTicket>",
        xml_text(user),
        xml_text(password_hash)
    )
}

/// The plain-text ticket: `TFIR<signature>\n<xml>`.
pub fn ticket_plain(xml: &str) -> String {
    format!("{TICKET_MAGIC}{TICKET_SIGNATURE}\n{xml}")
}

/// RC4 (symmetric: encrypting twice with the same key restores the input).
pub fn rc4(key: &[u8], data: &[u8]) -> Vec<u8> {
    assert!(!key.is_empty(), "RC4 key must not be empty");
    let mut s: [u8; 256] = std::array::from_fn(|i| i as u8);
    let mut j = 0u8;
    for i in 0..256 {
        j = j.wrapping_add(s[i]).wrapping_add(key[i % key.len()]);
        s.swap(i, j as usize);
    }
    let (mut i, mut j) = (0u8, 0u8);
    data.iter()
        .map(|b| {
            i = i.wrapping_add(1);
            j = j.wrapping_add(s[i as usize]);
            s.swap(i as usize, j as usize);
            b ^ s[s[i as usize].wrapping_add(s[j as usize]) as usize]
        })
        .collect()
}

/// The contents of the ticket file mapping: `key[8] | u32 LE length | RC4(key, plain)`.
pub fn mapping_bytes(key: &[u8; 8], plain: &[u8]) -> Vec<u8> {
    let enc = rc4(key, plain);
    let mut out = Vec::with_capacity(12 + enc.len());
    out.extend_from_slice(key);
    out.extend_from_slice(&(enc.len() as u32).to_le_bytes());
    out.extend_from_slice(&enc);
    out
}

/// Decodes [`mapping_bytes`] back to the plain ticket (for tests and `--dump-ticket`).
pub fn decode_mapping(bytes: &[u8]) -> Option<Vec<u8>> {
    let key = bytes.get(..8)?;
    let len = u32::from_le_bytes(bytes.get(8..12)?.try_into().ok()?) as usize;
    let enc = bytes.get(12..12 + len)?;
    Some(rc4(key, enc))
}

/// What to start the client with.
#[derive(Clone, Debug)]
pub struct LaunchOptions {
    pub login_host: String,
    pub login_port: u16,
    /// Client locale (`en_us`, `de`, `fr`, `ru`, `ko`, `ja`, `zh_cn`, ...); empty = none.
    pub lang: String,
    /// Extra client arguments (e.g. `-nosplash`), inserted before the HackShield argument.
    pub extra_args: Vec<String>,
    /// Append [`HSHIELD_ARG`] like the public launcher.
    pub hshield_arg: bool,
}

/// The client arguments (without the executable), as the public launcher builds them:
/// `-t +auth_ip HOST -auth_port PORT -handle MAP:EVENT [-lang L] [extra...] [+acpxmk]`.
pub fn client_args(opts: &LaunchOptions, map_handle: u32, event_handle: u32) -> Vec<String> {
    let mut a: Vec<String> = vec![
        "-t".into(),
        "+auth_ip".into(),
        opts.login_host.clone(),
        "-auth_port".into(),
        opts.login_port.to_string(),
        "-handle".into(),
        format!("{map_handle:08X}:{event_handle:08X}"),
    ];
    if !opts.lang.is_empty() {
        a.push("-lang".into());
        a.push(opts.lang.clone());
    }
    a.extend(opts.extra_args.iter().cloned());
    if opts.hshield_arg {
        a.push(HSHIELD_ARG.into());
    }
    a
}

/// Quotes one argument for a Windows command line (`CommandLineToArgvW` rules).
pub fn quote_arg(arg: &str) -> String {
    if !arg.is_empty() && !arg.contains([' ', '\t', '\n', '\x0b', '"']) {
        return arg.to_string();
    }
    let mut out = String::from("\"");
    let mut backslashes = 0usize;
    for c in arg.chars() {
        match c {
            '\\' => backslashes += 1,
            '"' => {
                out.extend(std::iter::repeat_n('\\', backslashes * 2 + 1));
                out.push('"');
                backslashes = 0;
            }
            _ => {
                out.extend(std::iter::repeat_n('\\', backslashes));
                out.push(c);
                backslashes = 0;
            }
        }
    }
    out.extend(std::iter::repeat_n('\\', backslashes * 2));
    out.push('"');
    out
}

/// The full command line: the quoted executable followed by the arguments.
pub fn command_line(exe: &str, args: &[String]) -> String {
    std::iter::once(quote_arg(exe))
        .chain(args.iter().map(|a| quote_arg(a)))
        .collect::<Vec<_>>()
        .join(" ")
}

#[cfg(windows)]
pub mod win;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn password_hash_is_lower_hex_sha256() {
        assert_eq!(
            password_hash("test"),
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
        );
    }

    #[test]
    fn ticket_xml_matches_public_launcher() {
        let xml = ticket_xml("alice", "abcd");
        assert_eq!(
            xml,
            "<?xml version=\"1.0\" encoding=\"UTF - 8\" standalone=\"yes\"?>\
             <authTicket version = \"1.2\"><storeToken>1</storeToken>\
             <username>alice</username><password>abcd</password></authTicket>"
        );
        assert_eq!(
            ticket_xml("a<b&c", "x"),
            "<?xml version=\"1.0\" encoding=\"UTF - 8\" standalone=\"yes\"?>\
             <authTicket version = \"1.2\"><storeToken>1</storeToken>\
             <username>a&lt;b&amp;c</username><password>x</password></authTicket>"
        );
        assert_eq!(ticket_plain("<x/>"), "TFIRdGVzdA==\n<x/>");
    }

    #[test]
    fn rc4_known_vectors() {
        // Classic test vectors (Wikipedia).
        assert_eq!(hex::encode(rc4(b"Key", b"Plaintext")), "bbf316e8d940af0ad3");
        assert_eq!(hex::encode(rc4(b"Wiki", b"pedia")), "1021bf0420");
        assert_eq!(
            hex::encode(rc4(b"Secret", b"Attack at dawn")),
            "45a01f645fc35b383552544b9bf5"
        );
    }

    #[test]
    fn mapping_layout_and_round_trip() {
        let key = [0x29, 0x6B, 0xD6, 0xEB, 0x2C, 0xA9, 0x03, 0x21];
        let plain = ticket_plain(&ticket_xml("user", &password_hash("pw")));
        let m = mapping_bytes(&key, plain.as_bytes());
        assert_eq!(&m[..8], &key);
        assert_eq!(
            u32::from_le_bytes(m[8..12].try_into().unwrap()) as usize,
            plain.len()
        );
        assert_eq!(m.len(), 12 + plain.len());
        assert_ne!(&m[12..], plain.as_bytes());
        assert_eq!(rc4(&key, &m[12..]), plain.as_bytes());
        assert_eq!(decode_mapping(&m).unwrap(), plain.as_bytes());
        assert!(decode_mapping(&m[..10]).is_none());
        assert!(decode_mapping(&m[..m.len() - 1]).is_none());
    }

    #[test]
    fn args_match_public_launcher() {
        let opts = LaunchOptions {
            login_host: "127.0.0.1".into(),
            login_port: 1237,
            lang: "en_us".into(),
            extra_args: vec!["-nosplash".into()],
            hshield_arg: true,
        };
        let a = client_args(&opts, 0x2a4, 0x1c);
        assert_eq!(
            a.join(" "),
            "-t +auth_ip 127.0.0.1 -auth_port 1237 -handle 000002A4:0000001C -lang en_us \
             -nosplash +acpxmk"
        );
        let opts = LaunchOptions {
            lang: String::new(),
            extra_args: vec![],
            hshield_arg: false,
            ..opts
        };
        assert_eq!(
            client_args(&opts, 1, 2).join(" "),
            "-t +auth_ip 127.0.0.1 -auth_port 1237 -handle 00000001:00000002"
        );
    }

    #[test]
    fn command_line_quoting() {
        assert_eq!(quote_arg("abc"), "abc");
        assert_eq!(quote_arg(""), "\"\"");
        assert_eq!(quote_arg("a b"), "\"a b\"");
        assert_eq!(quote_arg("a\"b"), "\"a\\\"b\"");
        assert_eq!(quote_arg("C:\\x y\\"), "\"C:\\x y\\\\\"");
        assert_eq!(
            command_line("C:\\Game Dir\\bin32\\archeage.exe", &["-t".into()]),
            "\"C:\\Game Dir\\bin32\\archeage.exe\" -t"
        );
    }
}
