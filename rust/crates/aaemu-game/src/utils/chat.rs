//! `Utils/Converters/ChatConverter`: the `|i...;` item links of chat messages.

const ALPHABET: &[u8; 65] = b"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz*+\0";

/// `ChatConverter.Encrypt(a1, a3, a4)`: packs the bits of `a4` (`a1` bytes) six at a time into
/// at most `a3` characters (C# operator precedence and shift-count masking kept).
fn encrypt(a1: i32, a3: i32, a4: &[u8]) -> String {
    let v9 = ALPHABET;
    let mut a2 = vec![0u8; a3.max(0) as usize];
    let mut v4: i32 = 0;
    let mut v5: i32 = 1;
    let mut v6: i32 = 2 * (i32::from(a4[0]) >> 7);
    let v10 = 8 * a1;
    let to_string = |a2: &[u8]| a2.iter().map(|&b| b as char).collect::<String>();
    let mut i: i32 = 0;
    while v5 < v10 {
        v4 = i;
        let bit = 7 - (v5 & 7);
        let v7 = (((1 << (bit & 31)) & i32::from(a4[(v5 >> 3) as usize])) >> (bit & 31)) | v6;
        if v5 % 6 == 5 {
            a2[i as usize] = v9[v7 as usize];
            v4 += 1;
            v6 = 0;
            i = v4;
            if v4 >= a3 {
                return to_string(&a2);
            }
        } else {
            v6 = 2 * v7;
        }
        v5 += 1;
    }
    if v5 % 6 != 0 {
        v4 += 1;
        let shift = (6 * v5 / 6 - (v5 & 0xFF) + 6) & 31;
        a2[(v4 - 1) as usize] = v9[(((v6 >> 1) << shift) as usize).min(64)];
        if v4 >= a3 {
            return to_string(&a2);
        }
    } else {
        a2[v4 as usize] = 0;
        return to_string(&a2);
    }
    String::new()
}

/// `ConvertAsChatMessageReference(templateId, grade, durability = 100)`.
pub fn convert_as_chat_message_reference(template_id: u32, grade: u8, durability: u8) -> String {
    let t = template_id.to_le_bytes();
    let mut data = [0u8; 192];
    data[8] = t[0];
    data[9] = t[1];
    data[12] = grade;
    data[16] = 0x01;
    data[24] = 0x01;
    data[29] = durability;
    let encrypted = encrypt(192, 384, &data);
    format!("|i{template_id},{grade},{};", encrypted.trim_matches('\0'))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn item_link_shape() {
        let s = convert_as_chat_message_reference(8000001, 2, 100);
        assert!(s.starts_with("|i8000001,2,"));
        assert!(s.ends_with(';'));
        // 192 bytes = 1536 bits -> 256 characters (trailing NULs trimmed).
        let body = &s["|i8000001,2,".len()..s.len() - 1];
        assert_eq!(body.len(), 256);
        assert!(body.bytes().all(|b| ALPHABET[..64].contains(&b)));
        // The template id bytes are encoded at bit offset 64 (character 10..).
        assert_ne!(
            convert_as_chat_message_reference(1, 0, 100),
            convert_as_chat_message_reference(2, 0, 100)
        );
    }
}
