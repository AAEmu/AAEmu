//! Local stdio MCP adapter for the real client driver. Stdout is exclusively JSON-RPC.
//! Protocol: https://modelcontextprotocol.io/specification/2025-11-25/basic/transports

use std::io::{BufRead, Read, Write};

use anyhow::{bail, Context};
use serde_json::{json, Value};
#[path = "client_actions.rs"]
mod actions;

const VERSION: &str = "2025-11-25";
const MAX_MESSAGE: u64 = 1 << 20;

fn tool(
    name: &str,
    description: &str,
    properties: Value,
    required: &[&str],
    read_only: bool,
) -> Value {
    json!({"name":name,"description":description,
        "inputSchema":{"type":"object","properties":properties,"required":required,"additionalProperties":false},
        "annotations":{"readOnlyHint":read_only,"openWorldHint":false}})
}

pub fn tools() -> Vec<Value> {
    let pid = json!({"type":"integer","minimum":1,"maximum":u32::MAX,"description":"Omit only when exactly one client is running."});
    let mut result = vec![
        tool("client_list", "List running ArcheAge clients and whether their driver is loaded.", json!({}), &[], true),
        tool("client_attach", "Inject the configured r208022 x86 driver into a running client. Does not modify client files.", json!({"pid":pid}), &[], false),
        tool("client_status", "Read driver, rendering and login stage.", json!({"pid":pid}), &[], true),
        tool("client_pak", "Control pak read-ahead in memory: optimize uses 64 KB, baseline restores 1 MB. profile starts/reset counters, status returns JSON, trace returns bounded CSV, off stops recording. verify compares actual engine reads across chunk boundaries, seeks and EOF. Does not modify assets.",
            json!({"pid":pid,"action":{"type":"string","enum":["optimize","baseline","profile","status","trace","off","verify"]}}), &["action"], false),
        tool("client_lua", "Run Lua on the client main thread; use the ui state for X2LoginCharacter and UI actions. Can mutate game state.",
            json!({"pid":pid,"target":{"type":"string","enum":["system","ui","third"]},"code":{"type":"string","minLength":1,"maxLength":262144}}), &["code"], false),
        tool("client_screenshot", "Capture a fresh DX10/DX11 rendered frame as PNG, even while the window is hidden. Returns an image and saved artifact path.",
            json!({"pid":pid}), &[], true),
        tool("client_window", "Show or hide the client without minimising it. Hidden rendering needs a windowed DX10/DX11 client.",
            json!({"pid":pid,"visible":{"type":"boolean"}}), &["visible"], false),
        tool("client_launch", "Launch another local 1.2 client with its own ticket and early driver injection. Silent by default; sound=true enables client sound and music. user and password_file override the session defaults; passwords are accepted only by filename. Use distinct accounts and target subsequent actions by the returned PID.",
            json!({"user":{"type":"string","minLength":1,"maxLength":32},"password_file":{"type":"string","minLength":1,"maxLength":32767},"pak_manifest":{"type":"string","minLength":1,"maxLength":32767},"fast_start":{"type":"boolean"},"fast_pak":{"type":"boolean"},"smooth_start":{"type":"boolean"},"hidden":{"type":"boolean"},"sound":{"type":"boolean","default":false}}), &[], false),
        tool("client_stop", "Stop a client launched by this MCP session. Existing user clients cannot be stopped by this tool.", json!({"pid":pid}), &["pid"], false),
    ];
    result.extend(actions::tools(pid));
    result
}

#[cfg(any(windows, test))]
fn launch_profile_args(args: &Value) -> Vec<&'static str> {
    let mut flags = Vec::new();
    for (key, on, off, default) in [
        ("fast_start", "--fast-start", "--no-fast-start", true),
        ("fast_pak", "--fast-pak", "--no-fast-pak", true),
        ("smooth_start", "--smooth-start", "--no-smooth-start", true),
        ("sound", "--sound", "--no-sound", false),
    ] {
        flags.push(if args[key].as_bool().unwrap_or(default) {
            on
        } else {
            off
        });
    }
    if args["hidden"].as_bool().unwrap_or(true) {
        flags.push("--hidden");
    }
    flags
}

fn validate(schema: &Value, args: &Value) -> anyhow::Result<()> {
    let object = args.as_object().context("arguments must be an object")?;
    let properties = schema["properties"]
        .as_object()
        .context("invalid tool schema")?;
    for required in schema["required"].as_array().into_iter().flatten() {
        let key = required.as_str().context("invalid required key")?;
        if !object.contains_key(key) {
            bail!("missing argument '{key}'");
        }
    }
    for (key, value) in object {
        let property = properties
            .get(key)
            .with_context(|| format!("unknown argument '{key}'"))?;
        let valid = match property["type"].as_str() {
            Some("integer") => value.as_u64().is_some_and(|n| {
                n >= property["minimum"].as_u64().unwrap_or(0)
                    && n <= property["maximum"].as_u64().unwrap_or(u64::MAX)
            }),
            Some("boolean") => value.is_boolean(),
            Some("array") => value.as_array().is_some_and(|items| {
                items.len() as u64 <= property["maxItems"].as_u64().unwrap_or(0)
                    && items.iter().all(|item| {
                        property["items"]["type"].as_str() == Some("string")
                            && item.is_string()
                            && property["items"]["enum"]
                                .as_array()
                                .is_some_and(|choices| choices.contains(item))
                    })
                    && (property["uniqueItems"] != true
                        || items
                            .iter()
                            .enumerate()
                            .all(|(i, item)| !items[..i].contains(item)))
            }),
            Some("string") => value.as_str().is_some_and(|s| {
                let len = s.chars().count() as u64;
                len >= property["minLength"].as_u64().unwrap_or(0)
                    && len <= property["maxLength"].as_u64().unwrap_or(u64::MAX)
                    && property["enum"]
                        .as_array()
                        .is_none_or(|choices| choices.contains(value))
            }),
            _ => false,
        };
        if !valid {
            bail!("invalid argument '{key}'");
        }
    }
    Ok(())
}

fn error(id: Value, code: i32, message: impl ToString) -> Value {
    json!({"jsonrpc":"2.0","id":id,"error":{"code":code,"message":message.to_string()}})
}

#[cfg(any(windows, test))]
fn text_result(text: impl ToString) -> Value {
    json!({"content":[{"type":"text","text":text.to_string()}],"isError":false})
}

#[derive(Default)]
struct Session {
    initialized: bool,
    ready: bool,
}

impl Session {
    fn dispatch(
        &mut self,
        message: Value,
        call: &mut impl FnMut(&str, &Value) -> anyhow::Result<Value>,
    ) -> Option<Value> {
        let id = message.get("id").cloned();
        let valid_id = id
            .as_ref()
            .is_none_or(|v| v.is_string() || v.as_i64().is_some() || v.as_u64().is_some());
        if !message.is_object()
            || message["jsonrpc"] != "2.0"
            || !message["method"].is_string()
            || !valid_id
        {
            return Some(error(Value::Null, -32600, "invalid JSON-RPC request"));
        }
        let method = message["method"].as_str()?;
        if id.is_none() {
            if method == "notifications/initialized" && self.initialized {
                self.ready = true;
            }
            return None;
        }
        let id = id?;
        let result = match method {
            "initialize" => {
                if self.initialized {
                    return Some(error(id, -32600, "session already initialized"));
                }
                let Some(version) = message["params"]["protocolVersion"].as_str() else {
                    return Some(error(id, -32602, "initialize needs protocolVersion"));
                };
                if !message["params"]["capabilities"].is_object()
                    || !message["params"]["clientInfo"].is_object()
                {
                    return Some(error(
                        id,
                        -32602,
                        "initialize needs capabilities and clientInfo",
                    ));
                }
                self.initialized = true;
                let version =
                    if [VERSION, "2025-06-18", "2025-03-26", "2024-11-05"].contains(&version) {
                        version
                    } else {
                        VERSION
                    };
                json!({"protocolVersion":version,"capabilities":{"tools":{}},
                    "serverInfo":{"name":"aaemu-client","version":env!("CARGO_PKG_VERSION")},
                    "instructions":"Controls the local ArcheAge 1.2 r208022 client. Use client_status and screenshots to verify Lua actions; client_launch defaults to silent audio, fast hidden rendering, normal-priority CPU calibration and 64 KB pak read-ahead. Set sound=true to enable client sound and music. For multiplayer, launch distinct user accounts and target actions with each returned PID; passwords come only from password_file or the configured file. Use client_pak for read profiling and byte verification. A hidden real client still needs Windows and a GPU. client_stop only stops this session's launches."})
            }
            "ping" => json!({}),
            _ if !self.ready => return Some(error(id, -32000, "initialize the session first")),
            "tools/list" => json!({"tools":tools()}),
            "tools/call" => {
                let params = &message["params"];
                let Some(name) = params["name"].as_str() else {
                    return Some(error(id, -32602, "missing tool name"));
                };
                let Some(tool) = tools().into_iter().find(|t| t["name"] == name) else {
                    return Some(error(id, -32602, "unknown tool"));
                };
                let args = params
                    .get("arguments")
                    .cloned()
                    .unwrap_or_else(|| json!({}));
                if let Err(e) = validate(&tool["inputSchema"], &args) {
                    return Some(error(id, -32602, e));
                }
                match call(name, &args) {
                    Ok(result) => result,
                    Err(e) => {
                        json!({"content":[{"type":"text","text":format!("{e:#}")}],"isError":true})
                    }
                }
            }
            _ => return Some(error(id, -32601, "method not found")),
        };
        Some(json!({"jsonrpc":"2.0","id":id,"result":result}))
    }
}

/// Newline-delimited JSON-RPC, including silent notifications and bounded request frames.
pub fn serve(
    mut input: impl BufRead,
    mut output: impl Write,
    mut call: impl FnMut(&str, &Value) -> anyhow::Result<Value>,
) -> anyhow::Result<()> {
    let mut session = Session::default();
    loop {
        let mut line = Vec::new();
        if input
            .by_ref()
            .take(MAX_MESSAGE + 1)
            .read_until(b'\n', &mut line)?
            == 0
        {
            return Ok(());
        }
        let response = if line.len() as u64 > MAX_MESSAGE {
            let response = error(Value::Null, -32600, "MCP request exceeds 1 MiB");
            serde_json::to_writer(&mut output, &response)?;
            writeln!(output)?;
            output.flush()?;
            bail!("MCP request exceeds 1 MiB");
        } else {
            match serde_json::from_slice(&line) {
                Ok(message) => session.dispatch(message, &mut call),
                Err(_) => Some(error(Value::Null, -32700, "invalid JSON")),
            }
        };
        if let Some(response) = response {
            serde_json::to_writer(&mut output, &response)?;
            writeln!(output)?;
            output.flush()?;
        }
    }
}

#[cfg(windows)]
mod backend {
    use super::*;
    use crate::{win, Request, Target};
    use base64::{engine::general_purpose::STANDARD, Engine};
    use std::collections::HashMap;
    use std::path::PathBuf;
    use std::time::{Duration, SystemTime, UNIX_EPOCH};

    pub struct Backend {
        dll: PathBuf,
        launcher: PathBuf,
        client: Option<PathBuf>,
        password_file: Option<PathBuf>,
        login: String,
        user: String,
        artifacts: PathBuf,
        owned: HashMap<u32, LaunchedClient>,
        sequence: u64,
    }

    struct LaunchedClient {
        process: win::OwnedClient,
        user: String,
    }

    impl Backend {
        pub fn parse() -> anyhow::Result<Self> {
            let exe = std::env::current_exe()?;
            let dir = exe.parent().context("executable has no parent")?;
            let mut out = Self {
                dll: std::env::var_os("AADRIVER_DLL")
                    .map(PathBuf::from)
                    .unwrap_or_else(|| dir.join("aadriver/aadriver.dll")),
                launcher: dir.join("aaemu-launcher.exe"),
                client: std::env::var_os("AAEMU_CLIENT").map(PathBuf::from),
                password_file: std::env::var_os("AAEMU_PASSWORD_FILE").map(PathBuf::from),
                login: "127.0.0.1:1237".into(),
                user: "clienttest".into(),
                artifacts: std::env::var_os("AAEMU_CLIENT_ARTIFACTS")
                    .map(PathBuf::from)
                    .unwrap_or_else(|| std::env::temp_dir().join("aaemu-client")),
                owned: HashMap::new(),
                sequence: 0,
            };
            let mut args = std::env::args().skip(2);
            while let Some(arg) = args.next() {
                let value = args
                    .next()
                    .with_context(|| format!("{arg} needs a value"))?;
                match arg.as_str() {
                    "--dll" => out.dll = value.into(),
                    "--launcher" => out.launcher = value.into(),
                    "--client" => out.client = Some(value.into()),
                    "--password-file" => out.password_file = Some(value.into()),
                    "--login" => out.login = value,
                    "--user" => out.user = value,
                    "--artifact-dir" => out.artifacts = value.into(),
                    _ => bail!("unknown MCP option {arg}"),
                }
            }
            Ok(out)
        }

        fn pid(&self, args: &Value) -> anyhow::Result<u32> {
            let clients = win::find_clients()?;
            if let Some(pid) = args["pid"].as_u64() {
                if clients.iter().any(|p| u64::from(p.pid) == pid) {
                    return Ok(pid as u32);
                }
                bail!("pid {pid} is not a running archeage.exe");
            }
            if clients.len() == 1 {
                return Ok(clients[0].pid);
            }
            bail!("{} clients running; supply pid", clients.len())
        }

        pub fn call(&mut self, name: &str, args: &Value) -> anyhow::Result<Value> {
            // Process handles distinguish original launches from later PID reuse.
            let exited: Vec<_> = self
                .owned
                .iter()
                .filter_map(|(&pid, client)| match client.process.is_running() {
                    Ok(false) => Some(Ok(pid)),
                    Ok(true) => None,
                    Err(error) => Some(Err(error)),
                })
                .collect::<anyhow::Result<_>>()?;
            for pid in exited {
                self.owned.remove(&pid);
            }
            if name == "client_list" {
                let mut out = Vec::new();
                for p in win::find_clients()? {
                    out.push(json!({"pid":p.pid,"exe":p.exe,"driver_loaded":win::is_driver_loaded(p.pid)?,"launched_by_session":self.owned.contains_key(&p.pid),"user":self.owned.get(&p.pid).map(|client| &client.user)}));
                }
                return Ok(text_result(serde_json::to_string(&out)?));
            }
            if name == "client_launch" {
                return self.launch(args);
            }
            let pid = self.pid(args)?;
            if name == "client_stop" {
                if !self.owned.contains_key(&pid) {
                    bail!("this session did not launch pid {pid}; refusing to stop an existing user client");
                }
                self.owned
                    .get(&pid)
                    .context("missing client ownership")?
                    .process
                    .stop()?;
                self.owned.remove(&pid);
                return Ok(text_result(format!("stopped pid {pid}")));
            }
            if name == "client_attach" && !win::is_driver_loaded(pid)? {
                win::inject(pid, &self.dll)?;
            }
            let mut driver = win::Driver::connect(
                pid,
                Duration::from_secs(if name == "client_attach" { 60 } else { 5 }),
            )?;
            match name {
                "client_perf" => {
                    let response = driver
                        .send(&Request::FrameProfile(
                            args["action"].as_str().context("missing action")?.into(),
                        ))?
                        .into_result()?;
                    let value: Value =
                        serde_json::from_str(&response).context("invalid frame profile")?;
                    Ok(
                        json!({"content":[{"type":"text","text":response}],"structuredContent":value,"isError":false}),
                    )
                }
                "client_input" => {
                    let command = actions::input_command(args)?;
                    Ok(text_result(
                        driver.send(&Request::Input(command))?.into_result()?,
                    ))
                }
                "client_pointer" => {
                    let command = actions::pointer_command(args)?;
                    Ok(text_result(
                        driver.send(&Request::Input(command))?.into_result()?,
                    ))
                }
                name if actions::is_tool(name) => {
                    let (target, code) = actions::request(name, args)?;
                    let response = driver.send(&Request::Lua(target, code))?.into_result()?;
                    let value: Value = serde_json::from_str(&response)
                        .context("client action returned invalid JSON")?;
                    Ok(
                        json!({"content":[{"type":"text","text":response}],"structuredContent":value,"isError":false}),
                    )
                }
                "client_attach" => Ok(text_result(driver.send(&Request::Ping)?.into_result()?)),
                "client_pak" => Ok(text_result(
                    driver
                        .send(&Request::PakIo(
                            args["action"].as_str().context("missing action")?.into(),
                        ))?
                        .into_result()?,
                )),
                "client_status" => {
                    let ping = driver.send(&Request::Ping)?.into_result()?;
                    let lua = driver.send(&Request::Lua(
                        Target::Ui,
                        "return X2LoginCharacter and X2LoginCharacter:GetCurrentStage()".into(),
                    ))?;
                    Ok(text_result(format!("{ping}\n{lua:?}")))
                }
                "client_lua" => {
                    let target = match args["target"].as_str().unwrap_or("system") {
                        "ui" => Target::Ui,
                        "third" => Target::Third,
                        _ => Target::System,
                    };
                    Ok(text_result(
                        driver
                            .send(&Request::Lua(
                                target,
                                args["code"].as_str().context("missing code")?.into(),
                            ))?
                            .into_result()?,
                    ))
                }
                "client_window" => Ok(text_result(
                    driver
                        .send(&Request::Window(
                            args["visible"].as_bool().context("missing visible")?,
                        ))?
                        .into_result()?,
                )),
                "client_screenshot" => {
                    let png = driver.screenshot()?;
                    std::fs::create_dir_all(&self.artifacts)?;
                    self.sequence += 1;
                    let ms = SystemTime::now().duration_since(UNIX_EPOCH)?.as_millis();
                    let path = self
                        .artifacts
                        .join(format!("client-{pid}-{ms}-{}.png", self.sequence));
                    std::fs::write(&path, &png)?;
                    let path = path.canonicalize()?;
                    Ok(
                        json!({"content":[{"type":"text","text":path.to_string_lossy()},
                        {"type":"image","mimeType":"image/png","data":STANDARD.encode(&png)}],"isError":false}),
                    )
                }
                _ => bail!("unknown tool {name}"),
            }
        }

        fn launch(&mut self, args: &Value) -> anyhow::Result<Value> {
            let user = args["user"].as_str().unwrap_or(&self.user);
            if self
                .owned
                .values()
                .any(|client| client.user.eq_ignore_ascii_case(user))
            {
                bail!("this session already launched account {user}; use a distinct user for multiplayer testing");
            }
            let client = self.client.as_ref().context("configure --client PATH")?;
            let password = args["password_file"]
                .as_str()
                .map(PathBuf::from)
                .or_else(|| self.password_file.clone())
                .context("configure --password-file PATH or supply password_file")?;
            // Validate all files before creating a client or inheritable auth ticket.
            for path in [client, &password, &self.dll, &self.launcher] {
                if !path.is_file() {
                    bail!("{} not found", path.display());
                }
            }
            let mut cmd = std::process::Command::new(&self.launcher);
            cmd.arg("--client")
                .arg(client)
                .arg("--login")
                .arg(&self.login)
                .arg("--user")
                .arg(user)
                .arg("--password-file")
                .arg(&password)
                .arg("--driver-dll")
                .arg(&self.dll)
                .arg("--multi-client");
            cmd.args(launch_profile_args(args));
            if let Some(manifest) = args["pak_manifest"].as_str() {
                let path = PathBuf::from(manifest);
                if !path.is_file() {
                    bail!("compressed pak manifest {} not found", path.display());
                }
                cmd.arg("--pak-manifest").arg(path);
            }
            let output = cmd.output().context("launching client")?;
            let stdout = String::from_utf8_lossy(&output.stdout);
            if let Some(pid) = stdout.lines().find_map(|line| {
                line.strip_prefix("client started (pid ")?
                    .strip_suffix(')')?
                    .parse::<u32>()
                    .ok()
            }) {
                // A rejected second instance may have exited before the launcher returns.
                if win::find_clients()?.iter().any(|client| client.pid == pid) {
                    self.owned.insert(
                        pid,
                        LaunchedClient {
                            process: win::OwnedClient::open(pid)?,
                            user: user.into(),
                        },
                    );
                }
            }
            if !output.status.success() {
                bail!(
                    "launcher failed: {}",
                    String::from_utf8_lossy(&output.stderr)
                );
            }
            let pid = stdout
                .lines()
                .find_map(|line| {
                    line.strip_prefix("client started (pid ")?
                        .strip_suffix(')')?
                        .parse::<u32>()
                        .ok()
                })
                .context("launcher did not report a client PID")?;
            if !self.owned.contains_key(&pid) {
                bail!("launched client pid {pid} exited; another instance may hold the client single-instance guard");
            }
            Ok(
                json!({"content":[{"type":"text","text":stdout}],"structuredContent":{"pid":pid,"user":user},"isError":false}),
            )
        }
    }
}

pub fn run() -> anyhow::Result<()> {
    #[cfg(windows)]
    {
        let mut backend = backend::Backend::parse()?;
        serve(
            std::io::stdin().lock(),
            std::io::stdout().lock(),
            |name, args| backend.call(name, args),
        )
    }
    #[cfg(not(windows))]
    {
        serve(std::io::stdin().lock(), std::io::stdout().lock(), |_, _| {
            bail!("client control requires Windows")
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn init() -> Value {
        json!({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":VERSION,"capabilities":{},"clientInfo":{"name":"test","version":"1"}}})
    }
    fn ready() -> Value {
        json!({"jsonrpc":"2.0","method":"notifications/initialized"})
    }
    fn run(messages: &[Value]) -> Vec<Value> {
        let mut input = String::new();
        for message in messages {
            input.push_str(&message.to_string());
            input.push('\n');
        }
        let mut output = Vec::new();
        serve(input.as_bytes(), &mut output, |name, _| {
            if name == "client_lua" {
                bail!("Lua execution failed");
            }
            Ok(text_result(name))
        })
        .unwrap();
        String::from_utf8(output)
            .unwrap()
            .lines()
            .map(|line| serde_json::from_str(line).unwrap())
            .collect()
    }
    #[test]
    fn lifecycle_notifications_and_discovery() {
        let results = run(&[
            init(),
            ready(),
            json!({"jsonrpc":"2.0","id":"list","method":"tools/list"}),
        ]);
        assert_eq!(results.len(), 2);
        assert_eq!(results[0]["result"]["protocolVersion"], VERSION);
        assert_eq!(results[1]["id"], "list");
        assert_eq!(results[1]["result"]["tools"].as_array().unwrap().len(), 20);
    }
    #[test]
    fn schema_errors_are_distinct_from_execution_errors() {
        let call = |args| json!({"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"client_lua","arguments":args}});
        for args in [
            json!({}),
            json!({"code":"x","pid":0}),
            json!({"code":"x","target":"bad"}),
            json!({"code":"x","password":"secret"}),
        ] {
            assert_eq!(
                run(&[init(), ready(), call(args)])[1]["error"]["code"],
                -32602
            );
        }
        let result = run(&[init(), ready(), call(json!({"code":"return 1"}))]);
        assert_eq!(result[1]["result"]["isError"], true);
    }
    #[test]
    fn rejects_calls_before_initialization_and_unknown_methods() {
        assert_eq!(
            run(&[json!({"jsonrpc":"2.0","id":3,"method":"tools/list"})])[0]["error"]["code"],
            -32000
        );
        assert_eq!(
            run(&[
                init(),
                ready(),
                json!({"jsonrpc":"2.0","id":3,"method":"bad"})
            ])[1]["error"]["code"],
            -32601
        );
    }
    #[test]
    fn launch_audio_defaults_to_silent_and_opt_in_preserves_other_profiles() {
        let defaults = launch_profile_args(&json!({}));
        assert_eq!(
            defaults,
            [
                "--fast-start",
                "--fast-pak",
                "--smooth-start",
                "--no-sound",
                "--hidden"
            ]
        );
        assert_eq!(launch_profile_args(&json!({"sound":false})), defaults);
        let audible = launch_profile_args(&json!({"sound":true,"hidden":false,"fast_pak":false}));
        assert_eq!(
            audible,
            ["--fast-start", "--no-fast-pak", "--smooth-start", "--sound"]
        );
        let launch = tools()
            .into_iter()
            .find(|t| t["name"] == "client_launch")
            .unwrap();
        assert!(validate(&launch["inputSchema"], &json!({"sound":true})).is_ok());
        assert!(validate(&launch["inputSchema"], &json!({"sound":false})).is_ok());
        assert!(validate(&launch["inputSchema"], &json!({"sound":"true"})).is_err());
    }

    #[test]
    fn pak_actions_and_launch_profiles_are_validated() {
        let pak = tools()
            .into_iter()
            .find(|t| t["name"] == "client_pak")
            .unwrap();
        for action in [
            "optimize", "baseline", "profile", "status", "trace", "off", "verify",
        ] {
            assert!(validate(&pak["inputSchema"], &json!({"action":action})).is_ok());
        }
        assert!(validate(&pak["inputSchema"], &json!({"action":"bad"})).is_err());
        assert!(validate(&pak["inputSchema"], &json!({})).is_err());
        let launch = tools()
            .into_iter()
            .find(|t| t["name"] == "client_launch")
            .unwrap();
        assert!(validate(&launch["inputSchema"], &json!({"fast_pak":false})).is_ok());
        assert!(validate(&launch["inputSchema"], &json!({"fast_pak":"false"})).is_err());
        assert!(validate(&launch["inputSchema"], &json!({"smooth_start":false})).is_ok());
        assert!(validate(&launch["inputSchema"], &json!({"smooth_start":"false"})).is_err());
        assert!(validate(
            &launch["inputSchema"],
            &json!({"user":"multitest_b","password_file":"D:/cfg/test.txt"})
        )
        .is_ok());
        assert!(validate(&launch["inputSchema"], &json!({"user":""})).is_err());
        assert!(validate(&launch["inputSchema"], &json!({"user":"a".repeat(33)})).is_err());
        assert!(validate(&launch["inputSchema"], &json!({"password_file":""})).is_err());
        assert!(validate(
            &launch["inputSchema"],
            &json!({"password":"never accept plaintext"})
        )
        .is_err());
    }
    #[test]
    fn malformed_input_and_oversized_input_are_bounded() {
        let mut output = Vec::new();
        serve(b"garbage\n".as_slice(), &mut output, |_, _| unreachable!()).unwrap();
        let value: Value = serde_json::from_slice(&output).unwrap();
        assert_eq!(value["error"]["code"], -32700);
        let huge = vec![b'x'; MAX_MESSAGE as usize + 1];
        assert!(serve(huge.as_slice(), Vec::new(), |_, _| unreachable!()).is_err());
    }
}
