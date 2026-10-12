//! Win32 side: finding the client process, talking to the driver pipe, and loading the driver
//! DLL into the (32-bit) client. Plain `kernel32` FFI, so the crate needs no bindings crate.
//!
//! Loading uses the standard `CreateRemoteThread(LoadLibraryW)` sequence — the same documented
//! mechanism the repo's existing `aa_debugger` prior art relies on. It is used here only against
//! the user's own client talking to a local test server; the client's files are never modified.

use std::ffi::c_void;
use std::fs::OpenOptions;
use std::path::Path;
use std::ptr::null_mut;
use std::time::{Duration, Instant};

use anyhow::{bail, Context};

use crate::{roundtrip, Reply, Request};

type Handle = *mut c_void;

const TH32CS_SNAPPROCESS: u32 = 0x2;
const PROCESS_ACCESS: u32 = 0x0002 /*CREATE_THREAD*/ | 0x0008 /*VM_OPERATION*/
    | 0x0010 /*VM_READ*/ | 0x0020 /*VM_WRITE*/ | 0x0400 /*QUERY_INFORMATION*/;
const MEM_COMMIT: u32 = 0x1000;
const MEM_RESERVE: u32 = 0x2000;
const MEM_RELEASE: u32 = 0x8000;
const PAGE_READWRITE: u32 = 0x04;
const LIST_MODULES_32BIT: u32 = 0x01;
const ERROR_PIPE_BUSY: i32 = 231;
const ERROR_FILE_NOT_FOUND: i32 = 2;

#[repr(C)]
struct ProcessEntry32W {
    dw_size: u32,
    cnt_usage: u32,
    th32_process_id: u32,
    th32_default_heap_id: usize,
    th32_module_id: u32,
    cnt_threads: u32,
    th32_parent_process_id: u32,
    pc_pri_class_base: i32,
    dw_flags: u32,
    sz_exe_file: [u16; 260],
}

#[link(name = "kernel32")]
extern "system" {
    fn CreateToolhelp32Snapshot(flags: u32, pid: u32) -> Handle;
    fn Process32FirstW(snap: Handle, e: *mut ProcessEntry32W) -> i32;
    fn Process32NextW(snap: Handle, e: *mut ProcessEntry32W) -> i32;
    fn CloseHandle(h: Handle) -> i32;
    fn OpenProcess(access: u32, inherit: i32, pid: u32) -> Handle;
    fn GetLastError() -> u32;
    fn IsWow64Process(h: Handle, wow64: *mut i32) -> i32;
    fn VirtualAllocEx(
        h: Handle,
        addr: *mut c_void,
        size: usize,
        typ: u32,
        protect: u32,
    ) -> *mut c_void;
    fn VirtualFreeEx(h: Handle, addr: *mut c_void, size: usize, typ: u32) -> i32;
    fn WriteProcessMemory(
        h: Handle,
        addr: *mut c_void,
        buf: *const c_void,
        n: usize,
        written: *mut usize,
    ) -> i32;
    fn CreateRemoteThread(
        h: Handle,
        sa: *mut c_void,
        stack: usize,
        start: *mut c_void,
        param: *mut c_void,
        flags: u32,
        tid: *mut u32,
    ) -> Handle;
    fn WaitForSingleObject(h: Handle, ms: u32) -> u32;
    fn GetExitCodeThread(h: Handle, code: *mut u32) -> i32;
    fn TerminateProcess(h: Handle, code: u32) -> i32;
    fn GetModuleFileNameExW(h: Handle, m: Handle, name: *mut u16, size: u32) -> u32;
}

#[link(name = "psapi")]
extern "system" {
    fn EnumProcessModulesEx(
        h: Handle,
        mods: *mut Handle,
        cb: u32,
        needed: *mut u32,
        filter: u32,
    ) -> i32;
    fn GetModuleBaseNameW(h: Handle, m: Handle, name: *mut u16, size: u32) -> u32;
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}
fn last_error(what: &str) -> anyhow::Error {
    let code = unsafe { GetLastError() };
    anyhow::anyhow!("{what} failed (Win32 error {code})")
}

struct OwnedHandle(Handle);
impl Drop for OwnedHandle {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe { CloseHandle(self.0) };
        }
    }
}

/// A retained handle to a client launched by the controller. Keeping the original process
/// object alive prevents a reused PID from granting ownership of an unrelated later client.
pub struct OwnedClient(OwnedHandle);

impl OwnedClient {
    pub fn is_running(&self) -> anyhow::Result<bool> {
        match unsafe { WaitForSingleObject(self.0 .0, 0) } {
            0 => Ok(false),
            0x102 => Ok(true),
            _ => Err(last_error("WaitForSingleObject")),
        }
    }

    pub fn open(pid: u32) -> anyhow::Result<Self> {
        if !find_clients()?.iter().any(|p| p.pid == pid) {
            bail!("pid {pid} is not an ArcheAge client");
        }
        let handle = unsafe {
            OpenProcess(
                1 /* PROCESS_TERMINATE */ | 0x00100000, /* SYNCHRONIZE */
                0,
                pid,
            )
        };
        if handle.is_null() {
            return Err(last_error("OpenProcess"));
        }
        Ok(Self(OwnedHandle(handle)))
    }

    pub fn stop(&self) -> anyhow::Result<()> {
        if unsafe { WaitForSingleObject(self.0 .0, 0) } == 0 {
            return Ok(());
        }
        if unsafe { TerminateProcess(self.0 .0, 0) } == 0 {
            return Err(last_error("TerminateProcess"));
        }
        // TerminateProcess is asynchronous: pending disk/GPU I/O must cancel before a
        // subsequent launch sees the client gone and can acquire its single-instance mutex.
        match unsafe { WaitForSingleObject(self.0 .0, 10_000) } {
            0 => {}
            u32::MAX => return Err(last_error("WaitForSingleObject")),
            _ => bail!("client termination did not finish within 10 seconds"),
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::os::windows::process::CommandExt;

    #[test]
    fn stop_waits_for_the_owned_process_and_is_idempotent() {
        let mut child = std::process::Command::new("powershell.exe")
            .args([
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "Start-Sleep -Seconds 30",
            ])
            .creation_flags(0x08000000 /* CREATE_NO_WINDOW */)
            .spawn()
            .unwrap();
        let handle = unsafe { OpenProcess(1 | 0x00100000, 0, child.id()) };
        assert!(!handle.is_null());
        let owned = OwnedClient(OwnedHandle(handle));
        assert!(owned.is_running().unwrap());
        owned.stop().unwrap();
        assert!(!owned.is_running().unwrap());
        assert!(child.try_wait().unwrap().is_some());
        owned.stop().unwrap();
    }
}

/// A running client process.
#[derive(Clone, Debug)]
pub struct ClientProcess {
    pub pid: u32,
    pub exe: String,
}

/// All processes whose executable base name is `archeage.exe` (case-insensitive).
pub fn find_clients() -> anyhow::Result<Vec<ClientProcess>> {
    let snap = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) };
    if snap as isize == -1 {
        return Err(last_error("CreateToolhelp32Snapshot"));
    }
    let snap = OwnedHandle(snap);
    let mut out = Vec::new();
    let mut e: ProcessEntry32W = unsafe { std::mem::zeroed() };
    e.dw_size = std::mem::size_of::<ProcessEntry32W>() as u32;
    let mut ok = unsafe { Process32FirstW(snap.0, &mut e) };
    while ok != 0 {
        let end = e.sz_exe_file.iter().position(|&c| c == 0).unwrap_or(0);
        let name = String::from_utf16_lossy(&e.sz_exe_file[..end]);
        if name.eq_ignore_ascii_case("archeage.exe") {
            out.push(ClientProcess {
                pid: e.th32_process_id,
                exe: name,
            });
        }
        ok = unsafe { Process32NextW(snap.0, &mut e) };
    }
    Ok(out)
}

/// Whether `pid` has `aadriver.dll` loaded (a 32-bit module).
pub fn is_driver_loaded(pid: u32) -> anyhow::Result<bool> {
    let h = unsafe { OpenProcess(PROCESS_ACCESS, 0, pid) };
    if h.is_null() {
        return Err(last_error("OpenProcess"));
    }
    let h = OwnedHandle(h);
    let mut mods = [null_mut::<c_void>(); 1024];
    let mut needed = 0u32;
    if unsafe {
        EnumProcessModulesEx(
            h.0,
            mods.as_mut_ptr(),
            std::mem::size_of_val(&mods) as u32,
            &mut needed,
            LIST_MODULES_32BIT,
        )
    } == 0
    {
        return Err(last_error("EnumProcessModulesEx"));
    }
    let count = (needed as usize / std::mem::size_of::<Handle>()).min(mods.len());
    for &m in &mods[..count] {
        if module_base_name(h.0, m).eq_ignore_ascii_case("aadriver.dll") {
            return Ok(true);
        }
    }
    Ok(false)
}

fn module_base_name(proc: Handle, m: Handle) -> String {
    let mut name = [0u16; 260];
    let n = unsafe { GetModuleBaseNameW(proc, m, name.as_mut_ptr(), name.len() as u32) };
    String::from_utf16_lossy(&name[..n as usize])
}

fn module_file_name(proc: Handle, m: Handle) -> String {
    let mut name = [0u16; 1024];
    let n = unsafe { GetModuleFileNameExW(proc, m, name.as_mut_ptr(), name.len() as u32) };
    String::from_utf16_lossy(&name[..n as usize])
}

/// The 32-bit `kernel32.dll` base address and on-disk path inside process `pid`.
fn remote_kernel32(h: Handle) -> anyhow::Result<(usize, String)> {
    let mut mods = [null_mut::<c_void>(); 1024];
    let mut needed = 0u32;
    if unsafe {
        EnumProcessModulesEx(
            h,
            mods.as_mut_ptr(),
            std::mem::size_of_val(&mods) as u32,
            &mut needed,
            LIST_MODULES_32BIT,
        )
    } == 0
    {
        return Err(last_error("EnumProcessModulesEx"));
    }
    let count = (needed as usize / std::mem::size_of::<Handle>()).min(mods.len());
    for &m in &mods[..count] {
        if module_base_name(h, m).eq_ignore_ascii_case("kernel32.dll") {
            return Ok((m as usize, module_file_name(h, m)));
        }
    }
    bail!("32-bit kernel32.dll not found in the client")
}

/// Loads `dll` into the 32-bit client `pid`.
///
/// Windows does not support `CreateRemoteThread` across the WOW64 boundary, so a 64-bit
/// controller cannot inject directly. When this controller is 64-bit it shells out to the 32-bit
/// `aainject.exe` built next to `aadriver.dll` (same directory, or `$AADRIVER_INJECT`); a 32-bit
/// controller (`i686-pc-windows-msvc`) injects directly with [`inject_direct`].
pub fn inject(pid: u32, dll: &Path) -> anyhow::Result<()> {
    if cfg!(target_pointer_width = "32") {
        return inject_direct(pid, dll);
    }
    let exe = find_injector(dll)?;
    let output = std::process::Command::new(&exe)
        .arg(pid.to_string())
        .arg(dll)
        .output()
        .with_context(|| format!("running {}", exe.display()))?;
    if !output.status.success() {
        bail!(
            "{} exited with {}: {}",
            exe.display(),
            output.status,
            String::from_utf8_lossy(&output.stderr)
        );
    }
    Ok(())
}

fn find_injector(dll: &Path) -> anyhow::Result<std::path::PathBuf> {
    if let Some(p) = std::env::var_os("AADRIVER_INJECT") {
        return Ok(p.into());
    }
    if let Some(dir) = dll.parent() {
        let c = dir.join("aainject.exe");
        if c.is_file() {
            return Ok(c);
        }
    }
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            for c in [
                dir.join("aainject.exe"),
                dir.join("aadriver").join("aainject.exe"),
            ] {
                if c.is_file() {
                    return Ok(c);
                }
            }
        }
    }
    bail!("aainject.exe not found next to the driver DLL or this exe; set $AADRIVER_INJECT")
}

/// Direct injection (only valid when this binary is itself 32-bit).
pub fn inject_direct(pid: u32, dll: &Path) -> anyhow::Result<()> {
    let h = unsafe { OpenProcess(PROCESS_ACCESS, 0, pid) };
    if h.is_null() {
        return Err(last_error("OpenProcess"))
            .context("cannot open the client (run as the same user / admin?)");
    }
    let h = OwnedHandle(h);

    let mut wow64 = 0i32;
    if unsafe { IsWow64Process(h.0, &mut wow64) } != 0 && wow64 == 0 {
        bail!("the client is not a 32-bit (WOW64) process; is this really the 1.2 client?");
    }

    // Resolve the client's 32-bit LoadLibraryW: base in the target + export RVA from disk.
    let (k32_base, k32_path) = remote_kernel32(h.0)?;
    let k32_image = std::fs::read(&k32_path).with_context(|| format!("reading {k32_path}"))?;
    let rva = crate::export_rva(&k32_image, "LoadLibraryW")
        .context("finding LoadLibraryW in the client's kernel32")?;
    let load = (k32_base + rva as usize) as *mut c_void;

    let full = dll
        .canonicalize()
        .with_context(|| format!("resolving {}", dll.display()))?;
    // CreateProcessW gives the client a path with the \\?\ prefix off canonicalize; strip it.
    let path = full.to_string_lossy().replace(r"\\?\", "");
    let wpath = wide(&path);
    let bytes = wpath.len() * 2;

    let remote = unsafe {
        VirtualAllocEx(
            h.0,
            null_mut(),
            bytes,
            MEM_COMMIT | MEM_RESERVE,
            PAGE_READWRITE,
        )
    };
    if remote.is_null() {
        return Err(last_error("VirtualAllocEx"));
    }
    let free = scopeguard(|| unsafe {
        VirtualFreeEx(h.0, remote, 0, MEM_RELEASE);
    });
    if unsafe {
        WriteProcessMemory(
            h.0,
            remote,
            wpath.as_ptr() as *const c_void,
            bytes,
            null_mut(),
        )
    } == 0
    {
        return Err(last_error("WriteProcessMemory"));
    }

    let thread = unsafe { CreateRemoteThread(h.0, null_mut(), 0, load, remote, 0, null_mut()) };
    if thread.is_null() {
        return Err(last_error("CreateRemoteThread"));
    }
    let thread = OwnedHandle(thread);
    if unsafe { WaitForSingleObject(thread.0, 30_000) } != 0 {
        // The remote thread can still read the path. Retain it until process exit.
        std::mem::forget(free);
        bail!("LoadLibraryW did not finish within 30 s; client left running");
    }
    let mut code = 0u32;
    unsafe { GetExitCodeThread(thread.0, &mut code) };
    drop(free);
    if code == 0 {
        bail!("LoadLibraryW in the client returned 0 (the DLL failed to load; check its bitness / deps)");
    }
    Ok(())
}

fn scopeguard<F: FnMut()>(f: F) -> impl Drop {
    struct G<F: FnMut()>(F);
    impl<F: FnMut()> Drop for G<F> {
        fn drop(&mut self) {
            (self.0)()
        }
    }
    G(f)
}

/// A connection to the driver's named pipe.
pub struct Driver {
    file: std::fs::File,
    pub pid: u32,
}

impl Driver {
    /// Connects to the driver in process `pid`, retrying while the pipe is busy / not yet up.
    pub fn connect(pid: u32, timeout: Duration) -> anyhow::Result<Driver> {
        let name = crate::pipe_name(pid);
        let deadline = Instant::now() + timeout;
        loop {
            match OpenOptions::new().read(true).write(true).open(&name) {
                Ok(file) => return Ok(Driver { file, pid }),
                Err(e) => {
                    let raw = e.raw_os_error().unwrap_or(0);
                    if (raw == ERROR_PIPE_BUSY || raw == ERROR_FILE_NOT_FOUND)
                        && Instant::now() < deadline
                    {
                        std::thread::sleep(Duration::from_millis(100));
                        continue;
                    }
                    return Err(anyhow::Error::new(e))
                        .with_context(|| format!("opening {name} (is the driver loaded?)"));
                }
            }
        }
    }

    /// One request / reply round trip.
    pub fn send(&mut self, req: &Request) -> anyhow::Result<Reply> {
        Ok(roundtrip(&mut self.file, req)?)
    }

    /// Returns PNG bytes from the client's renderer, independent of desktop visibility.
    pub fn screenshot(&mut self) -> anyhow::Result<Vec<u8>> {
        crate::write_frame(&mut self.file, &Request::Screenshot.payload())?;
        crate::screenshot_reply(&crate::read_frame(&mut self.file)?)
    }
}
