//! `Scripts/Commands/Announce.cs`.
//!
//! usage: /announce <NoticeType> <Color> <VisibleTime> <message>
//! usage example: /announce 3 FFFF00 2500 Text here is in yellow

use super::prelude::*;
use crate::systems::services::shutdown::SCNoticeMessagePacket;

/// `Color.Lime`.
const LIME: Color = Color::argb(255, 0, 255, 0);

/// The `System.Drawing.KnownColor` web colors `Color.FromName` resolves (case-insensitive).
const KNOWN_COLORS: &[(&str, u32)] = &[
    ("transparent", 0x00FFFFFF),
    ("aliceblue", 0xFFF0F8FF),
    ("antiquewhite", 0xFFFAEBD7),
    ("aqua", 0xFF00FFFF),
    ("aquamarine", 0xFF7FFFD4),
    ("azure", 0xFFF0FFFF),
    ("beige", 0xFFF5F5DC),
    ("bisque", 0xFFFFE4C4),
    ("black", 0xFF000000),
    ("blanchedalmond", 0xFFFFEBCD),
    ("blue", 0xFF0000FF),
    ("blueviolet", 0xFF8A2BE2),
    ("brown", 0xFFA52A2A),
    ("burlywood", 0xFFDEB887),
    ("cadetblue", 0xFF5F9EA0),
    ("chartreuse", 0xFF7FFF00),
    ("chocolate", 0xFFD2691E),
    ("coral", 0xFFFF7F50),
    ("cornflowerblue", 0xFF6495ED),
    ("cornsilk", 0xFFFFF8DC),
    ("crimson", 0xFFDC143C),
    ("cyan", 0xFF00FFFF),
    ("darkblue", 0xFF00008B),
    ("darkcyan", 0xFF008B8B),
    ("darkgoldenrod", 0xFFB8860B),
    ("darkgray", 0xFFA9A9A9),
    ("darkgreen", 0xFF006400),
    ("darkkhaki", 0xFFBDB76B),
    ("darkmagenta", 0xFF8B008B),
    ("darkolivegreen", 0xFF556B2F),
    ("darkorange", 0xFFFF8C00),
    ("darkorchid", 0xFF9932CC),
    ("darkred", 0xFF8B0000),
    ("darksalmon", 0xFFE9967A),
    ("darkseagreen", 0xFF8FBC8F),
    ("darkslateblue", 0xFF483D8B),
    ("darkslategray", 0xFF2F4F4F),
    ("darkturquoise", 0xFF00CED1),
    ("darkviolet", 0xFF9400D3),
    ("deeppink", 0xFFFF1493),
    ("deepskyblue", 0xFF00BFFF),
    ("dimgray", 0xFF696969),
    ("dodgerblue", 0xFF1E90FF),
    ("firebrick", 0xFFB22222),
    ("floralwhite", 0xFFFFFAF0),
    ("forestgreen", 0xFF228B22),
    ("fuchsia", 0xFFFF00FF),
    ("gainsboro", 0xFFDCDCDC),
    ("ghostwhite", 0xFFF8F8FF),
    ("gold", 0xFFFFD700),
    ("goldenrod", 0xFFDAA520),
    ("gray", 0xFF808080),
    ("green", 0xFF008000),
    ("greenyellow", 0xFFADFF2F),
    ("honeydew", 0xFFF0FFF0),
    ("hotpink", 0xFFFF69B4),
    ("indianred", 0xFFCD5C5C),
    ("indigo", 0xFF4B0082),
    ("ivory", 0xFFFFFFF0),
    ("khaki", 0xFFF0E68C),
    ("lavender", 0xFFE6E6FA),
    ("lavenderblush", 0xFFFFF0F5),
    ("lawngreen", 0xFF7CFC00),
    ("lemonchiffon", 0xFFFFFACD),
    ("lightblue", 0xFFADD8E6),
    ("lightcoral", 0xFFF08080),
    ("lightcyan", 0xFFE0FFFF),
    ("lightgoldenrodyellow", 0xFFFAFAD2),
    ("lightgray", 0xFFD3D3D3),
    ("lightgreen", 0xFF90EE90),
    ("lightpink", 0xFFFFB6C1),
    ("lightsalmon", 0xFFFFA07A),
    ("lightseagreen", 0xFF20B2AA),
    ("lightskyblue", 0xFF87CEFA),
    ("lightslategray", 0xFF778899),
    ("lightsteelblue", 0xFFB0C4DE),
    ("lightyellow", 0xFFFFFFE0),
    ("lime", 0xFF00FF00),
    ("limegreen", 0xFF32CD32),
    ("linen", 0xFFFAF0E6),
    ("magenta", 0xFFFF00FF),
    ("maroon", 0xFF800000),
    ("mediumaquamarine", 0xFF66CDAA),
    ("mediumblue", 0xFF0000CD),
    ("mediumorchid", 0xFFBA55D3),
    ("mediumpurple", 0xFF9370DB),
    ("mediumseagreen", 0xFF3CB371),
    ("mediumslateblue", 0xFF7B68EE),
    ("mediumspringgreen", 0xFF00FA9A),
    ("mediumturquoise", 0xFF48D1CC),
    ("mediumvioletred", 0xFFC71585),
    ("midnightblue", 0xFF191970),
    ("mintcream", 0xFFF5FFFA),
    ("mistyrose", 0xFFFFE4E1),
    ("moccasin", 0xFFFFE4B5),
    ("navajowhite", 0xFFFFDEAD),
    ("navy", 0xFF000080),
    ("oldlace", 0xFFFDF5E6),
    ("olive", 0xFF808000),
    ("olivedrab", 0xFF6B8E23),
    ("orange", 0xFFFFA500),
    ("orangered", 0xFFFF4500),
    ("orchid", 0xFFDA70D6),
    ("palegoldenrod", 0xFFEEE8AA),
    ("palegreen", 0xFF98FB98),
    ("paleturquoise", 0xFFAFEEEE),
    ("palevioletred", 0xFFDB7093),
    ("papayawhip", 0xFFFFEFD5),
    ("peachpuff", 0xFFFFDAB9),
    ("peru", 0xFFCD853F),
    ("pink", 0xFFFFC0CB),
    ("plum", 0xFFDDA0DD),
    ("powderblue", 0xFFB0E0E6),
    ("purple", 0xFF800080),
    ("rebeccapurple", 0xFF663399),
    ("red", 0xFFFF0000),
    ("rosybrown", 0xFFBC8F8F),
    ("royalblue", 0xFF4169E1),
    ("saddlebrown", 0xFF8B4513),
    ("salmon", 0xFFFA8072),
    ("sandybrown", 0xFFF4A460),
    ("seagreen", 0xFF2E8B57),
    ("seashell", 0xFFFFF5EE),
    ("sienna", 0xFFA0522D),
    ("silver", 0xFFC0C0C0),
    ("skyblue", 0xFF87CEEB),
    ("slateblue", 0xFF6A5ACD),
    ("slategray", 0xFF708090),
    ("snow", 0xFFFFFAFA),
    ("springgreen", 0xFF00FF7F),
    ("steelblue", 0xFF4682B4),
    ("tan", 0xFFD2B48C),
    ("teal", 0xFF008080),
    ("thistle", 0xFFD8BFD8),
    ("tomato", 0xFFFF6347),
    ("turquoise", 0xFF40E0D0),
    ("violet", 0xFFEE82EE),
    ("wheat", 0xFFF5DEB3),
    ("white", 0xFFFFFFFF),
    ("whitesmoke", 0xFFF5F5F5),
    ("yellow", 0xFFFFFF00),
    ("yellowgreen", 0xFF9ACD32),
];

/// `Color.FromName(name)` for the web colors (`None` = an unknown name, which .NET returns as
/// an all-zero color).
fn from_name(name: &str) -> Option<Color> {
    KNOWN_COLORS
        .iter()
        .find(|(n, _)| n.eq_ignore_ascii_case(name))
        .map(|(_, v)| {
            let [a, r, g, b] = v.to_be_bytes();
            Color::argb(a, r, g, b)
        })
}

/// `byte.Parse(text, NumberStyles.HexNumber)`.
fn parse_hex_byte(chars: &[char]) -> Option<u8> {
    let s: String = chars.iter().collect();
    let t = s.trim_matches(|c: char| c.is_ascii_whitespace());
    if t.is_empty() || !t.chars().all(|c| c.is_ascii_hexdigit()) {
        return None;
    }
    u8::from_str_radix(t, 16).ok()
}

/// `NameOrHexColor(hex)`: a color name (unless it is pure black), else `[AA]RRGGBB` with an
/// optional `#`; white on any error.
pub fn name_or_hex_color(hex: &str) -> Color {
    let parse = || -> Option<Color> {
        if let Some(c) = from_name(&hex.to_lowercase()) {
            if c.r != 0 || c.g != 0 || c.b != 0 {
                return Some(c); // Only accept try by name when it didn't return pure black
            }
        }
        // remove the # at the front
        let chars: Vec<char> = hex.replace('#', "").chars().collect();
        let mut a = 255u8;
        let mut start = 0;
        // handle ARGB strings (8 characters long)
        if chars.len() == 8 {
            a = parse_hex_byte(&chars[0..2])?;
            start = 2;
        }
        // convert RGB characters to bytes (`Substring` throws when too short)
        let r = parse_hex_byte(chars.get(start..start + 2)?)?;
        let g = parse_hex_byte(chars.get(start + 2..start + 4)?)?;
        let b = parse_hex_byte(chars.get(start + 4..start + 6)?)?;
        Some(Color::argb(a, r, g, b))
    };
    parse().unwrap_or(Color::WHITE) // Return white on errors
}

/// `Announce`.
pub struct Announce;

impl Announce {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Announce {
    fn command_names(&self) -> Vec<String> {
        names(&["announce"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // if no arguments send help information
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let result = (|| -> anyhow::Result<()> {
            let mut message_type = try_parse::<u8>(&args[0]).unwrap_or(0);
            let mut message_color = LIME;
            let mut message_visible_time = 0;
            let mut first_arg = 3;
            if !(1..=3).contains(&message_type) {
                // Invalid type, assume the user only provided text, and handle everything
                // automatically
                first_arg = 0;
                message_type = 3;
            } else {
                let index_error = || anyhow::anyhow!("Index was outside the bounds of the array.");
                message_color = name_or_hex_color(args.get(1).ok_or_else(index_error)?);
                if let Some(v) = try_parse::<i32>(args.get(2).ok_or_else(index_error)?) {
                    message_visible_time = v;
                }
            }
            let mut message = String::new();
            for a in args.iter().skip(first_arg) {
                message += a;
                message += " ";
            }
            // broadcast to all online clients in server
            ctx.server()?
                .world
                .broadcast_to_server(&SCNoticeMessagePacket::new(
                    message_type,
                    message_color,
                    message_visible_time,
                    &message,
                ));
            // send back confirmation script executed to script runner
            CommandManager::send_normal_text(self, out, "Announcement sent.");
            Ok(())
        })();
        if let Err(x) = result {
            CommandManager::send_error_text(self, out, &format!("Exception: {x}"));
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<NoticeType> <Color> <VisibleTime> <message>".into()
    }

    fn get_command_help_text(&self) -> String {
        format!(
            "Broadcasts a server-wide message in a given style.\r\n\
             <NoticeType> is a value from 1-3 (default 3), \
             <Color> is a RGB or ARGB value in Hex, \
             <VisibleTime> is in milliseconds\r\n\
             If the first arguments isn't a valid NoticeType, all arguments are treated as the message using default color and time settings for type 3.\r\n\
             examples:\r\n\
             {p}announce 3 FF00FF00 5000 Text here use as many spaces as you wish\r\n\
             {p}announce 3 FFFF00 2500 Text here is in yellow\r\n\
             {p}announce 3 red 2500 Text here is in red\r\n\
             {p}announce Automatically generated lime text",
            p = COMMAND_PREFIX
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::*;
    use crate::opcodes::sc;

    #[test]
    fn colors() {
        assert_eq!(name_or_hex_color("Red"), Color::argb(255, 255, 0, 0));
        assert_eq!(name_or_hex_color("#FFFF00"), Color::argb(255, 255, 255, 0));
        assert_eq!(
            name_or_hex_color("80112233"),
            Color::argb(0x80, 0x11, 0x22, 0x33)
        );
        // Black by name is refused, then parsed as hex: error -> white.
        assert_eq!(name_or_hex_color("black"), Color::WHITE);
        assert_eq!(name_or_hex_color("000000"), Color::argb(255, 0, 0, 0));
        assert_eq!(name_or_hex_color("12"), Color::WHITE);
    }

    #[tokio::test]
    async fn announce_broadcasts_notices() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut other = harness::pc(&server, 2, "Other", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "announce 3 FFFF00 2500 Hello there").await;
        assert_eq!(out, ["[announce] Announcement sent."]);
        let expected =
            SCNoticeMessagePacket::new(3, Color::argb(255, 255, 255, 0), 2500, "Hello there ");
        let body = crate::net::packet::encode(&expected)[6..].to_vec();
        for pc in [&mut gm, &mut other] {
            assert_eq!(
                pc.packets(),
                vec![(sc::SCNoticeMessagePacket, body.clone())]
            );
        }
        // Plain text: type 3, lime, automatic time.
        harness::run(&server, &gm.p, "announce Hi all").await;
        let expected = SCNoticeMessagePacket::new(3, LIME, 0, "Hi all ");
        let body = crate::net::packet::encode(&expected)[6..].to_vec();
        assert_eq!(other.frames(), vec![(sc::SCNoticeMessagePacket, body)]);
        // A valid type without the color / time arguments.
        let (_, out) = harness::run(&server, &gm.p, "announce 2").await;
        assert_eq!(
            out,
            [harness::error(
                "announce",
                "Exception: Index was outside the bounds of the array."
            )]
        );
    }
}
