use aaemu_pak_bridge::{apply, build, diff, profile, Manifest, Pak, DEFAULT_CHUNK_SIZE};
use anyhow::{bail, Context, Result};
use std::path::PathBuf;

const HELP: &str = "aaemu-pak: lossless original-byte virtual pak and offline updates

  build SOURCE DIRECTORY [--chunk-size BYTES]
      Write DIRECTORY/game.aacm and immutable SHA256.aacb segments.
      Default chunks: 262144 bytes; power of two from 4096 to 4194304.
      Refuses an existing manifest. Never modifies SOURCE.
  diff NEW_SOURCE BASE_MANIFEST PATCH_DIRECTORY
      Reuse identical base chunks at any logical offset; package only new
      blob data, game.aacm and receipt.json binding exact base/new manifest hashes.
  profile BASE_MANIFEST READ_TRACE_CSV PATCH_DIRECTORY
      Package traced compressed chunks as raw storage; logical bytes/SHA stay exact.
      Native CSV header: offset,bytes,microseconds,thread. Maximum 64 MiB,
      1000000 rows; reads must fit logical bounds, zero-byte reads are skipped.
      Cold chunks reuse existing blobs. Already-raw/empty selection is a verified
      no-op. Apply offline as below; old compressed blobs remain for rollback,
      so this experiment adds disk space and does not guarantee faster loading.
  apply INSTALLED_MANIFEST PATCH_DIRECTORY
      Offline only: Windows refuses active native clients; stop clients first.
      Verify all new blobs and logical bytes,
      then atomically replace the manifest. Old blobs remain for rollback.
      Reapplying the same patch is a verified no-op; a different base is refused.
  verify MANIFEST
      Check segment hashes, every decoded chunk, and whole logical SHA256.
  info MANIFEST
      Print logical size, chunk/segment counts and hashes as JSON.

The logical stream is bit-identical to SOURCE, including offsets, gaps, encrypted
file table and footer. This does not resize, convert or decrypt game assets.
";

fn run() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.is_empty() || matches!(args[0].as_str(), "--help" | "-h" | "help") {
        print!("{HELP}");
        return Ok(());
    }
    match args[0].as_str() {
        "build" if args.len() == 3 || args.len() == 5 => {
            let size = if args.len() == 5 {
                if args[3] != "--chunk-size" {
                    bail!("expected --chunk-size BYTES");
                }
                args[4].parse().context("invalid chunk size")?
            } else {
                DEFAULT_CHUNK_SIZE
            };
            let path = build(&args[1], &args[2], size)?;
            println!("{}", path.display());
        }
        "diff" if args.len() == 4 => {
            println!("{}", diff(&args[1], &args[2], &args[3])?.display());
        }
        "profile" if args.len() == 4 => {
            println!("{}", profile(&args[1], &args[2], &args[3])?.display());
        }
        "apply" if args.len() == 3 => {
            println!("{:?}", apply(&args[1], &args[2])?);
        }
        "verify" if args.len() == 2 => {
            let pak = Pak::open(&args[1])?;
            pak.verify()?;
            println!(
                "verified {} logical bytes, SHA256 {}",
                pak.manifest.logical_size,
                hex::encode(pak.manifest.logical_hash)
            );
        }
        "info" if args.len() == 2 => {
            let manifest = Manifest::load(PathBuf::from(&args[1]))?;
            println!(
                "{}",
                serde_json::json!({"logical_size":manifest.logical_size,"logical_sha256":hex::encode(manifest.logical_hash),"chunk_size":manifest.chunk_size,"chunks":manifest.chunks.len(),"segments":manifest.segments.len(),"stored_bytes":manifest.segments.iter().map(|s|s.stored_length).sum::<u64>()})
            );
        }
        _ => bail!("invalid arguments; run aaemu-pak --help"),
    }
    Ok(())
}
fn main() {
    if let Err(error) = run() {
        eprintln!("aaemu-pak: {error:#}");
        std::process::exit(1);
    }
}
