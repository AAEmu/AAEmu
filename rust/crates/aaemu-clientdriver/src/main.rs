//! `aaemu-clientdriver` — operate a running ArcheAge 1.2 client by running Lua in it.
//!
//! ```text
//! aaemu-clientdriver list                         list running clients and driver status
//! aaemu-clientdriver inject [--dll PATH] [--pid N] load the in-client driver (aadriver.dll)
//! aaemu-clientdriver ping   [--pid N]              driver / engine info
//! aaemu-clientdriver lua    [--target system|ui|third] [--pid N] <code...>|-   run Lua, print result
//! aaemu-clientdriver exec   [--target ...] [--pid N] <code...>|-   IScriptSystem::ExecuteBuffer
//! aaemu-clientdriver repl   [--target ...] [--pid N]               interactive line REPL
//! aaemu-clientdriver unload [--pid N]              unhook and unload the driver
//! ```
//!
//! With no `--pid`, the single running client is used (ambiguous if more than one). `inject`
//! finds `aadriver.dll` next to the executable, under `--dll`, or at `$AADRIVER_DLL`.
//! See `docs/client/LUA-DRIVER.md`.

use std::io::{BufRead, Write};
use std::path::PathBuf;
use std::time::Duration;

use aaemu_clientdriver::{Reply, Request, Target};
use anyhow::{bail, Context};

#[cfg(windows)]
use aaemu_clientdriver::win::{find_clients, inject, is_driver_loaded, Driver};

struct Args {
    cmd: String,
    pid: Option<u32>,
    target: Target,
    dll: Option<PathBuf>,
    rest: Vec<String>,
}

fn parse() -> anyhow::Result<Args> {
    let mut a = Args {
        cmd: String::new(),
        pid: None,
        target: Target::System,
        dll: std::env::var_os("AADRIVER_DLL").map(PathBuf::from),
        rest: vec![],
    };
    let mut it = std::env::args().skip(1);
    a.cmd = it.next().unwrap_or_else(|| "help".into());
    while let Some(arg) = it.next() {
        match arg.as_str() {
            "--pid" => a.pid = Some(it.next().context("--pid needs a value")?.parse()?),
            "--dll" => a.dll = Some(it.next().context("--dll needs a value")?.into()),
            "--target" | "-t" => {
                a.target = match it.next().context("--target needs a value")?.as_str() {
                    "system" | "s" => Target::System,
                    "ui" | "u" => Target::Ui,
                    "third" | "t" => Target::Third,
                    other => bail!("unknown target '{other}' (system|ui|third)"),
                }
            }
            _ => a.rest.push(arg),
        }
    }
    Ok(a)
}

/// The Lua / exec code: the positional args joined, or stdin when it is `-` or empty.
fn code(a: &Args) -> anyhow::Result<String> {
    use std::io::Read;
    if a.rest.is_empty() || a.rest == ["-"] {
        let mut s = String::new();
        std::io::stdin().read_to_string(&mut s)?;
        Ok(s)
    } else {
        Ok(a.rest.join(" "))
    }
}

#[cfg(windows)]
fn resolve_pid(a: &Args) -> anyhow::Result<u32> {
    if let Some(p) = a.pid {
        return Ok(p);
    }
    let clients = find_clients()?;
    match clients.len() {
        0 => bail!("no running archeage.exe found"),
        1 => Ok(clients[0].pid),
        _ => bail!(
            "{} clients running; pick one with --pid ({})",
            clients.len(),
            clients
                .iter()
                .map(|c| c.pid.to_string())
                .collect::<Vec<_>>()
                .join(", ")
        ),
    }
}

#[cfg(windows)]
fn driver_dll(a: &Args) -> anyhow::Result<PathBuf> {
    if let Some(d) = &a.dll {
        return Ok(d.clone());
    }
    let exe = std::env::current_exe()?;
    let dir = exe.parent().context("exe has no parent")?;
    for c in [
        dir.join("aadriver.dll"),
        dir.join("aadriver").join("aadriver.dll"),
    ] {
        if c.is_file() {
            return Ok(c);
        }
    }
    bail!("aadriver.dll not found next to the exe; pass --dll PATH or set AADRIVER_DLL")
}

fn print_reply(r: Reply) -> anyhow::Result<()> {
    match r {
        Reply::Ok(s) => {
            println!("{s}");
            Ok(())
        }
        Reply::LuaError(e) => {
            eprintln!("lua error: {e}");
            std::process::exit(1)
        }
        Reply::DriverError(e) => {
            eprintln!("driver error: {e}");
            std::process::exit(2)
        }
    }
}

#[cfg(windows)]
fn run() -> anyhow::Result<()> {
    let a = parse()?;
    match a.cmd.as_str() {
        "mcp" => aaemu_clientdriver::mcp::run(),
        "list" => {
            let clients = find_clients()?;
            if clients.is_empty() {
                println!("no running archeage.exe");
            }
            for c in clients {
                let loaded = is_driver_loaded(c.pid).unwrap_or(false);
                println!(
                    "pid {}  {}  driver={}",
                    c.pid,
                    c.exe,
                    if loaded { "loaded" } else { "-" }
                );
            }
            Ok(())
        }
        "inject" => {
            let pid = resolve_pid(&a)?;
            if is_driver_loaded(pid)? {
                println!("driver already loaded in pid {pid}");
                return Ok(());
            }
            let dll = driver_dll(&a)?;
            inject(pid, &dll)?;
            println!("injected {} into pid {pid}", dll.display());
            let mut d = Driver::connect(pid, Duration::from_secs(30))?;
            print_reply(d.send(&Request::Ping)?)
        }
        "ping" => {
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            print_reply(d.send(&Request::Ping)?)
        }
        "pak" => {
            let action = a.rest.first().map(String::as_str).unwrap_or("status");
            if ![
                "optimize", "baseline", "profile", "status", "trace", "off", "verify",
            ]
            .contains(&action)
            {
                bail!("pak expects optimize, baseline, profile, status, trace, off or verify");
            }
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            print_reply(d.send(&Request::PakIo(action.into()))?)
        }
        "screenshot" => {
            let path = a
                .rest
                .first()
                .context("screenshot needs an output PNG path")?;
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            std::fs::write(path, d.screenshot()?)?;
            println!("{path}");
            Ok(())
        }
        "window" => {
            let visible = match a.rest.first().map(String::as_str) {
                Some("show") => true,
                Some("hide") => false,
                _ => bail!("window needs show or hide"),
            };
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            print_reply(d.send(&Request::Window(visible))?)
        }
        "lua" => {
            let c = code(&a)?;
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            print_reply(d.send(&Request::Lua(a.target, c))?)
        }
        "exec" => {
            let c = code(&a)?;
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            print_reply(d.send(&Request::ExecuteBuffer(a.target, c))?)
        }
        "unload" => {
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            print_reply(d.send(&Request::Unload)?)
        }
        "repl" => {
            let mut d = Driver::connect(resolve_pid(&a)?, Duration::from_secs(5))?;
            eprintln!(
                "aaemu-clientdriver repl (target {:?}); empty line / Ctrl-Z to quit",
                a.target
            );
            let stdin = std::io::stdin();
            loop {
                eprint!("lua> ");
                std::io::stderr().flush().ok();
                let mut line = String::new();
                if stdin.lock().read_line(&mut line)? == 0 {
                    break;
                }
                let line = line.trim_end();
                if line.is_empty() {
                    break;
                }
                match d.send(&Request::Lua(a.target, line.to_string()))? {
                    Reply::Ok(s) => println!("{s}"),
                    Reply::LuaError(e) => eprintln!("! {e}"),
                    Reply::DriverError(e) => eprintln!("!! {e}"),
                }
            }
            Ok(())
        }
        "help" | "-h" | "--help" => {
            print!("{}", include_str!("usage.txt"));
            Ok(())
        }
        other => bail!("unknown command '{other}' (try `help`)"),
    }
}

#[cfg(not(windows))]
fn run() -> anyhow::Result<()> {
    bail!("aaemu-clientdriver only works on Windows (it drives the Win32 client)")
}

fn main() {
    let result = if std::env::args().nth(1).as_deref() == Some("mcp") {
        aaemu_clientdriver::mcp::run()
    } else {
        run()
    };
    if let Err(e) = result {
        eprintln!("error: {e:#}");
        std::process::exit(2);
    }
}
