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
    /// Creates `archeage_auth_ticket_map` with `bytes` (see [`crate::mapping_bytes`]) and
    /// `archeage_auth_ticket_event`, both inheritable.
    pub fn create(bytes: &[u8]) -> anyhow::Result<Self> {
        let sa = SecurityAttributes {
            n_length: std::mem::size_of::<SecurityAttributes>() as u32,
            lp_security_descriptor: null_mut(),
            b_inherit_handle: 1,
        };
        let size = u32::try_from(bytes.len()).context("ticket too large")?;
        let map_name = wide(crate::TICKET_MAP_NAME);
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
        let event_name = wide(crate::TICKET_EVENT_NAME);
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
    _thread: OwnedHandle,
    pub pid: u32,
}

impl Process {
    /// Starts `command_line` (see [`crate::command_line`]) with handle inheritance in `cwd`.
    pub fn spawn(exe: &Path, command_line: &str, cwd: &Path) -> anyhow::Result<Self> {
        if !exe.is_file() {
            bail!("client executable {} not found", exe.display());
        }
        let app = wide(&exe.to_string_lossy());
        let mut cmd = wide(command_line);
        let dir = wide(&cwd.to_string_lossy());
        // SAFETY: zeroed is a valid STARTUPINFOW / PROCESS_INFORMATION (all integers / nulls).
        let mut si: StartupInfoW = unsafe { std::mem::zeroed() };
        si.cb = std::mem::size_of::<StartupInfoW>() as u32;
        let mut pi: ProcessInformation = unsafe { std::mem::zeroed() };
        let ok = unsafe {
            CreateProcessW(
                app.as_ptr(),
                cmd.as_mut_ptr(),
                null(),
                null(),
                1,
                CREATE_UNICODE_ENVIRONMENT,
                null(),
                dir.as_ptr(),
                &si,
                &mut pi,
            )
        };
        if ok == 0 {
            return Err(last_error("CreateProcessW"));
        }
        Ok(Self {
            process: OwnedHandle(pi.h_process),
            _thread: OwnedHandle(pi.h_thread),
            pid: pi.dw_process_id,
        })
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ticket_mapping_is_readable_through_the_handle() {
        let bytes = crate::mapping_bytes(&[1, 2, 3, 4, 5, 6, 7, 8], b"TFIRdGVzdA==\n<x/>");
        let t = Ticket::create(&bytes).unwrap();
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
}
