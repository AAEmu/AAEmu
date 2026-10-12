//! Win32 side of the launcher: the inheritable ticket mapping + event and `CreateProcessW` with
//! handle inheritance. Plain `kernel32` FFI, so the crate needs no Windows bindings crate.

use std::ffi::c_void;
use std::path::Path;
use std::ptr::{null, null_mut};

use anyhow::{bail, Context};

type Handle = *mut c_void;

const INVALID_HANDLE_VALUE: Handle = -1isize as Handle;
const PAGE_READWRITE: u32 = 0x04;
const FILE_MAP_ALL_ACCESS: u32 = 0x000F_001F;
const ERROR_ALREADY_EXISTS: u32 = 183;
const WAIT_OBJECT_0: u32 = 0;
const WAIT_TIMEOUT: u32 = 0x102;
const INFINITE: u32 = 0xFFFF_FFFF;
const CREATE_UNICODE_ENVIRONMENT: u32 = 0x400;
const CREATE_SUSPENDED: u32 = 0x04;
const SYNCHRONIZE: u32 = 0x0010_0000;
const EXTENDED_STARTUPINFO_PRESENT: u32 = 0x80000;
const PROC_THREAD_ATTRIBUTE_HANDLE_LIST: usize = 0x20002;

#[repr(C)]
struct SecurityAttributes {
    n_length: u32,
    lp_security_descriptor: *mut c_void,
    b_inherit_handle: i32,
}

#[repr(C)]
struct StartupInfoW {
    cb: u32,
    lp_reserved: *mut u16,
    lp_desktop: *mut u16,
    lp_title: *mut u16,
    dw_x: u32,
    dw_y: u32,
    dw_x_size: u32,
    dw_y_size: u32,
    dw_x_count_chars: u32,
    dw_y_count_chars: u32,
    dw_fill_attribute: u32,
    dw_flags: u32,
    w_show_window: u16,
    cb_reserved2: u16,
    lp_reserved2: *mut u8,
    h_std_input: Handle,
    h_std_output: Handle,
    h_std_error: Handle,
}

#[repr(C)]
struct ProcessInformation {
    h_process: Handle,
    h_thread: Handle,
    dw_process_id: u32,
    dw_thread_id: u32,
}

#[repr(C)]
struct StartupInfoExW {
    startup: StartupInfoW,
    attributes: *mut c_void,
}

#[link(name = "kernel32")]
extern "system" {
    fn CreateFileMappingW(
        h_file: Handle,
        attributes: *const SecurityAttributes,
        protect: u32,
        max_size_high: u32,
        max_size_low: u32,
        name: *const u16,
    ) -> Handle;
    fn MapViewOfFile(
        h: Handle,
        access: u32,
        offset_high: u32,
        offset_low: u32,
        bytes: usize,
    ) -> *mut c_void;
    fn UnmapViewOfFile(address: *const c_void) -> i32;
    fn CreateEventW(
        attributes: *const SecurityAttributes,
        manual_reset: i32,
        initial_state: i32,
        name: *const u16,
    ) -> Handle;
    fn CloseHandle(h: Handle) -> i32;
    fn OpenEventW(access: u32, inherit: i32, name: *const u16) -> Handle;
    fn ResumeThread(thread: Handle) -> u32;
    fn TerminateProcess(process: Handle, exit_code: u32) -> i32;
    fn GetLastError() -> u32;
    fn WaitForSingleObject(h: Handle, milliseconds: u32) -> u32;
    fn GetExitCodeProcess(h: Handle, exit_code: *mut u32) -> i32;
    fn CreateProcessW(
        application_name: *const u16,
        command_line: *mut u16,
        process_attributes: *const SecurityAttributes,
        thread_attributes: *const SecurityAttributes,
        inherit_handles: i32,
        creation_flags: u32,
        environment: *const c_void,
        current_directory: *const u16,
        startup_info: *const StartupInfoW,
        process_information: *mut ProcessInformation,
    ) -> i32;
    fn InitializeProcThreadAttributeList(
        list: *mut c_void,
        count: u32,
        flags: u32,
        size: *mut usize,
    ) -> i32;
    fn UpdateProcThreadAttribute(
        list: *mut c_void,
        flags: u32,
        attribute: usize,
        value: *mut c_void,
        size: usize,
        previous: *mut c_void,
        returned: *mut usize,
    ) -> i32;
    fn DeleteProcThreadAttributeList(list: *mut c_void);
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

fn last_error(what: &str) -> anyhow::Error {
    let code = unsafe { GetLastError() };
    anyhow::anyhow!(
        "{what} failed (Win32 error {code}: {})",
        std::io::Error::from_raw_os_error(code as i32)
    )
}

/// An owned kernel handle.
pub struct OwnedHandle(Handle);

impl OwnedHandle {
    /// The numeric value passed on the client command line.
    pub fn value(&self) -> u32 {
        self.0 as usize as u32
    }
}

impl Drop for OwnedHandle {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}

/// The inheritable ticket mapping + event. Keep it alive until the client has read the ticket.
pub struct Ticket {
    pub map: OwnedHandle,
    pub event: OwnedHandle,
    /// The objects already existed (another launcher / client instance holds them).
    pub already_existed: bool,
}

impl Ticket {
    /// Creates a private ticket mapping and event, both inheritable. The client receives
    /// their handles through `-handle`; unique names avoid overwriting another client's
    /// still-open ticket or accepting its already-signalled event.
    pub fn create(bytes: &[u8]) -> anyhow::Result<Self> {
        let suffix = format!("{}-{:016x}", std::process::id(), rand::random::<u64>());
        Self::create_named(
            bytes,
            &format!("{}-{suffix}", crate::TICKET_MAP_NAME),
            &format!("{}-{suffix}", crate::TICKET_EVENT_NAME),
        )
    }

    fn create_named(bytes: &[u8], mapping_name: &str, event_name: &str) -> anyhow::Result<Self> {
        let sa = SecurityAttributes {
            n_length: std::mem::size_of::<SecurityAttributes>() as u32,
            lp_security_descriptor: null_mut(),
            b_inherit_handle: 1,
        };
        let size = u32::try_from(bytes.len()).context("ticket too large")?;
        let map_name = wide(mapping_name);
        // SAFETY: plain Win32 calls with valid, NUL-terminated names and a live `sa`.
        let map = unsafe {
            CreateFileMappingW(
                INVALID_HANDLE_VALUE,
                &sa,
                PAGE_READWRITE,
                0,
                size,
                map_name.as_ptr(),
            )
        };
        if map.is_null() {
            return Err(last_error("CreateFileMappingW"));
        }
        let mut already_existed = unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
        let map = OwnedHandle(map);
        let view = unsafe { MapViewOfFile(map.0, FILE_MAP_ALL_ACCESS, 0, 0, bytes.len()) };
        if view.is_null() {
            return Err(last_error("MapViewOfFile"));
        }
        // SAFETY: the view is at least `bytes.len()` bytes long (the mapping was created with
        // that size, or is an existing one we just mapped that many bytes of).
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), view.cast::<u8>(), bytes.len());
            UnmapViewOfFile(view);
        }
        let event_name = wide(event_name);
        let event = unsafe { CreateEventW(&sa, 1, 0, event_name.as_ptr()) };
        if event.is_null() {
            return Err(last_error("CreateEventW"));
        }
        already_existed |= unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
        Ok(Self {
            map,
            event: OwnedHandle(event),
            already_existed,
        })
    }

    /// Waits until the client signals the ticket event; `false` on timeout.
    pub fn wait_signaled(&self, timeout_ms: u32) -> anyhow::Result<bool> {
        match unsafe { WaitForSingleObject(self.event.0, timeout_ms) } {
            WAIT_OBJECT_0 => Ok(true),
            WAIT_TIMEOUT => Ok(false),
            _ => Err(last_error("WaitForSingleObject")),
        }
    }
}

/// A started client process.
pub struct Process {
    process: OwnedHandle,
    thread: OwnedHandle,
    suspended: bool,
    pub pid: u32,
}

impl Process {
    /// Starts `command_line` (see [`crate::command_line`]) with handle inheritance in `cwd`.
    pub fn spawn(
        exe: &Path,
        command_line: &str,
        cwd: &Path,
        ticket: &Ticket,
        hidden: bool,
        suspended: bool,
    ) -> anyhow::Result<Self> {
        if !exe.is_file() {
            bail!("client executable {} not found", exe.display());
        }
        let app = wide(&exe.to_string_lossy());
        let mut cmd = wide(command_line);
        let dir = wide(&cwd.to_string_lossy());
        // Inherit only the auth ticket. Inheriting the launcher's stdout/stderr pipe handles
        // makes Command::output wait until the *client* exits, blocking MCP client_launch.
        let mut size = 0;
        unsafe { InitializeProcThreadAttributeList(null_mut(), 1, 0, &mut size) };
        if size == 0 {
            return Err(last_error("InitializeProcThreadAttributeList"));
        }
        let mut storage = vec![0usize; size.div_ceil(std::mem::size_of::<usize>())];
        let attributes = storage.as_mut_ptr().cast::<c_void>();
        if unsafe { InitializeProcThreadAttributeList(attributes, 1, 0, &mut size) } == 0 {
            return Err(last_error("InitializeProcThreadAttributeList"));
        }
        struct AttributeList(*mut c_void);
        impl Drop for AttributeList {
            fn drop(&mut self) {
                unsafe { DeleteProcThreadAttributeList(self.0) };
            }
        }
        let _attributes = AttributeList(attributes);
        let mut handles = [ticket.map.0, ticket.event.0];
        if unsafe {
            UpdateProcThreadAttribute(
                attributes,
                0,
                PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                handles.as_mut_ptr().cast(),
                std::mem::size_of_val(&handles),
                null_mut(),
                null_mut(),
            )
        } == 0
        {
            return Err(last_error("UpdateProcThreadAttribute"));
        }
        // SAFETY: zeroed is valid; the attribute list and its handle array stay live through spawn.
        let mut si: StartupInfoExW = unsafe { std::mem::zeroed() };
        si.startup.cb = std::mem::size_of::<StartupInfoExW>() as u32;
        if hidden {
            si.startup.dw_flags = 1; // STARTF_USESHOWWINDOW: keep the initial GUI hidden too.
            si.startup.w_show_window = 0; // SW_HIDE
        }
        si.attributes = attributes;
        let mut pi: ProcessInformation = unsafe { std::mem::zeroed() };
        let ok = unsafe {
            CreateProcessW(
                app.as_ptr(),
                cmd.as_mut_ptr(),
                null(),
                null(),
                1,
                CREATE_UNICODE_ENVIRONMENT
                    | EXTENDED_STARTUPINFO_PRESENT
                    | if suspended { CREATE_SUSPENDED } else { 0 },
                null(),
                dir.as_ptr(),
                &si.startup,
                &mut pi,
            )
        };
        if ok == 0 {
            return Err(last_error("CreateProcessW"));
        }
        Ok(Self {
            process: OwnedHandle(pi.h_process),
            thread: OwnedHandle(pi.h_thread),
            suspended,
            pid: pi.dw_process_id,
        })
    }

    /// The injected policy publishes readiness only after its own process audio
    /// sessions are configured. Never resume the client before this handshake.
    pub fn wait_for_audio_ready(&self, timeout_ms: u32) -> anyhow::Result<()> {
        let name = wide(&format!("Local\\aaemu-client-audio-ready-{}", self.pid));
        let deadline =
            std::time::Instant::now() + std::time::Duration::from_millis(u64::from(timeout_ms));
        loop {
            let event = unsafe { OpenEventW(SYNCHRONIZE, 0, name.as_ptr()) };
            if !event.is_null() {
                let event = OwnedHandle(event);
                if unsafe { WaitForSingleObject(event.0, 0) } == WAIT_OBJECT_0 {
                    return Ok(());
                }
            }
            if std::time::Instant::now() >= deadline {
                bail!(
                    "driver did not prepare this client's audio sessions; client was not resumed"
                );
            }
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
    }

    pub fn resume(&mut self) -> anyhow::Result<()> {
        if self.suspended {
            if unsafe { ResumeThread(self.thread.0) } == u32::MAX {
                return Err(last_error("ResumeThread"));
            }
            self.suspended = false;
        }
        Ok(())
    }

    /// Waits up to `timeout_ms` (`None` = forever); returns the exit code once it has exited.
    pub fn wait(&self, timeout_ms: Option<u32>) -> anyhow::Result<Option<u32>> {
        match unsafe { WaitForSingleObject(self.process.0, timeout_ms.unwrap_or(INFINITE)) } {
            WAIT_OBJECT_0 => {
                let mut code = 0u32;
                if unsafe { GetExitCodeProcess(self.process.0, &mut code) } == 0 {
                    return Err(last_error("GetExitCodeProcess"));
                }
                Ok(Some(code))
            }
            WAIT_TIMEOUT => Ok(None),
            _ => Err(last_error("WaitForSingleObject")),
        }
    }
}

impl Drop for Process {
    fn drop(&mut self) {
        // A failed injection/policy handshake must not leave a suspended orphan
        // or fall back to a noisy client. This handle belongs only to our spawn.
        if self.suspended {
            unsafe { TerminateProcess(self.process.0, 1) };
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn client_remains_suspended_until_audio_ready_and_explicit_resume() {
        let cmd = std::path::PathBuf::from(std::env::var_os("WINDIR").unwrap())
            .join("System32")
            .join("cmd.exe");
        let cwd = cmd.parent().unwrap();
        let ticket = Ticket::create(b"audio readiness fixture").unwrap();
        // cmd.exe has its own command grammar; quoted "/c" is not a switch.
        let line = format!("\"{}\" /d /c exit 0", cmd.display());
        let mut process = Process::spawn(&cmd, &line, cwd, &ticket, true, true).unwrap();
        assert!(process.wait_for_audio_ready(1).is_err());
        assert_eq!(process.wait(Some(0)).unwrap(), None);
        let name = wide(&format!("Local\\aaemu-client-audio-ready-{}", process.pid));
        let ready = unsafe { CreateEventW(null(), 1, 1, name.as_ptr()) };
        assert!(!ready.is_null());
        let _ready = OwnedHandle(ready);
        process.wait_for_audio_ready(50).unwrap();
        assert_eq!(process.wait(Some(0)).unwrap(), None);
        process.resume().unwrap();
        assert_eq!(process.wait(Some(5_000)).unwrap(), Some(0));
    }

    #[test]
    fn ticket_mapping_is_readable_through_the_handle() {
        let bytes = crate::mapping_bytes(&[1, 2, 3, 4, 5, 6, 7, 8], b"TFIRdGVzdA==\n<x/>");
        // Never touch the live client's globally named ticket during cargo test.
        let suffix = format!("test-{}", std::process::id());
        let t = Ticket::create_named(
            &bytes,
            &format!("aaemu-map-{suffix}"),
            &format!("aaemu-event-{suffix}"),
        )
        .unwrap();
        assert_ne!(t.map.value(), 0);
        assert_ne!(t.event.value(), 0);
        let view = unsafe { MapViewOfFile(t.map.0, FILE_MAP_ALL_ACCESS, 0, 0, bytes.len()) };
        assert!(!view.is_null());
        let read = unsafe { std::slice::from_raw_parts(view.cast::<u8>(), bytes.len()).to_vec() };
        unsafe { UnmapViewOfFile(view) };
        assert_eq!(read, bytes);
        assert_eq!(
            crate::decode_mapping(&read).unwrap(),
            b"TFIRdGVzdA==\n<x/>".to_vec()
        );
        assert!(!t.wait_signaled(0).unwrap());
    }

    #[test]
    fn simultaneous_tickets_keep_authentication_and_events_separate() {
        let first = Ticket::create(b"first account").unwrap();
        let second = Ticket::create(b"second account").unwrap();
        assert!(!first.already_existed && !second.already_existed);
        assert_ne!(first.map.value(), second.map.value());
        for (ticket, expected) in [
            (&first, b"first account".as_slice()),
            (&second, b"second account".as_slice()),
        ] {
            let view =
                unsafe { MapViewOfFile(ticket.map.0, FILE_MAP_ALL_ACCESS, 0, 0, expected.len()) };
            assert!(!view.is_null());
            assert_eq!(
                unsafe { std::slice::from_raw_parts(view.cast::<u8>(), expected.len()) },
                expected
            );
            unsafe { UnmapViewOfFile(view) };
            assert!(!ticket.wait_signaled(0).unwrap());
        }
    }
}
