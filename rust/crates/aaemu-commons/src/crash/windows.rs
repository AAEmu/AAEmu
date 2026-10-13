//! Fatal native exceptions on Windows x64: an unhandled-exception filter + a reporter thread.
//!
//! The filter runs on the crashing thread, which after a stack overflow only has the few KiB
//! of `SetThreadStackGuarantee` reserve left (std sets it for its threads). So it only stores
//! the exception pointers, wakes the reporter thread (own stack, started at install time) and
//! waits for it. The reporter unwinds the crashed thread's saved context with
//! `RtlVirtualUnwind`, symbolizes the frames with dbghelp, appends the report to `Crash.log`,
//! writes a minidump and lets the filter return `EXCEPTION_CONTINUE_SEARCH`, so the process
//! then terminates as before (Windows Error Reporting still sees the crash). Plain `kernel32` /
//! `dbghelp` FFI, like `aaemu-launcher/src/win.rs`.

use std::collections::HashMap;
use std::ffi::c_void;
use std::fmt::Write as _;
use std::os::windows::io::AsRawHandle;
use std::ptr::{null, null_mut};
use std::sync::atomic::{AtomicBool, AtomicPtr, AtomicU32, Ordering};

type Handle = *mut c_void;

const INFINITE: u32 = 0xFFFF_FFFF;
/// How long the crashing thread waits for the report (symbol loading can take a while).
const REPORT_TIMEOUT_MS: u32 = 120_000;
const EXCEPTION_CONTINUE_SEARCH: i32 = 0;
const EXCEPTION_STACK_OVERFLOW: u32 = 0xC000_00FD;
const EXCEPTION_ACCESS_VIOLATION: u32 = 0xC000_0005;
const THREAD_QUERY_LIMITED_INFORMATION: u32 = 0x0800;
const GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS: u32 = 0x4;
const GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT: u32 = 0x2;
const SYMOPT_UNDNAME: u32 = 0x02;
const SYMOPT_DEFERRED_LOADS: u32 = 0x04;
const SYMOPT_LOAD_LINES: u32 = 0x10;
const SYMOPT_FAIL_CRITICAL_ERRORS: u32 = 0x200;
const MINIDUMP_WITH_UNLOADED_MODULES: u32 = 0x20;
const MINIDUMP_WITH_INDIRECTLY_REFERENCED_MEMORY: u32 = 0x40;
const MINIDUMP_WITH_THREAD_INFO: u32 = 0x1000;
/// Frames walked at most (a stack overflow of an 8 MiB stack is a few 10k frames).
const MAX_FRAMES: usize = 200_000;
/// Frames printed from the top and the bottom of a long stack.
const HEAD_FRAMES: usize = 96;
const TAIL_FRAMES: usize = 48;
/// Functions listed in the "most frequent" summary of a long stack.
const SUMMARY_FUNCTIONS: usize = 12;

/// `CONTEXT_AMD64`: opaque apart from the registers the unwinder reads.
#[repr(C, align(16))]
#[derive(Clone, Copy)]
struct Context([u8; 0x4D0]);

impl Context {
    const RSP: usize = 0x98;
    const RIP: usize = 0xF8;

    fn reg(&self, offset: usize) -> u64 {
        u64::from_ne_bytes(self.0[offset..offset + 8].try_into().expect("8 bytes"))
    }

    fn set_reg(&mut self, offset: usize, value: u64) {
        self.0[offset..offset + 8].copy_from_slice(&value.to_ne_bytes());
    }
}

#[repr(C)]
struct ExceptionRecord {
    code: u32,
    flags: u32,
    record: *mut ExceptionRecord,
    address: *mut c_void,
    number_parameters: u32,
    information: [usize; 15],
}

#[repr(C)]
struct ExceptionPointers {
    record: *mut ExceptionRecord,
    context: *mut Context,
}

#[repr(C)]
struct RuntimeFunction {
    begin: u32,
    end: u32,
    unwind: u32,
}

#[repr(C, packed(4))]
struct MinidumpExceptionInformation {
    thread_id: u32,
    pointers: *mut ExceptionPointers,
    client_pointers: i32,
}

/// `SYMBOL_INFOW` with room for the name (`SizeOfStruct` is the fixed part, 88 bytes).
#[repr(C)]
struct SymbolInfo {
    size_of_struct: u32,
    type_index: u32,
    reserved: [u64; 2],
    index: u32,
    size: u32,
    mod_base: u64,
    flags: u32,
    value: u64,
    address: u64,
    register: u32,
    scope: u32,
    tag: u32,
    name_len: u32,
    max_name_len: u32,
    name: [u16; 1024],
}

/// `IMAGEHLP_LINEW64`.
#[repr(C)]
struct LineInfo {
    size_of_struct: u32,
    key: *mut c_void,
    line_number: u32,
    file_name: *const u16,
    address: u64,
}

#[link(name = "kernel32")]
extern "system" {
    fn SetUnhandledExceptionFilter(
        filter: Option<unsafe extern "system" fn(*mut ExceptionPointers) -> i32>,
    ) -> *mut c_void;
    fn CreateEventW(
        attrs: *mut c_void,
        manual_reset: i32,
        initial: i32,
        name: *const u16,
    ) -> Handle;
    fn SetEvent(event: Handle) -> i32;
    fn WaitForSingleObject(handle: Handle, ms: u32) -> u32;
    fn GetCurrentThreadId() -> u32;
    fn GetCurrentProcess() -> Handle;
    fn GetCurrentProcessId() -> u32;
    fn OpenThread(access: u32, inherit: i32, thread_id: u32) -> Handle;
    fn CloseHandle(handle: Handle) -> i32;
    fn GetThreadDescription(thread: Handle, description: *mut *mut u16) -> i32;
    fn LocalFree(mem: *mut c_void) -> *mut c_void;
    fn GetModuleHandleExW(flags: u32, address: *const c_void, module: *mut Handle) -> i32;
    fn GetModuleFileNameW(module: Handle, name: *mut u16, size: u32) -> u32;
    fn RtlLookupFunctionEntry(
        pc: u64,
        image_base: *mut u64,
        history: *mut c_void,
    ) -> *mut RuntimeFunction;
    fn RtlVirtualUnwind(
        handler_type: u32,
        image_base: u64,
        pc: u64,
        function: *mut RuntimeFunction,
        context: *mut Context,
        handler_data: *mut *mut c_void,
        establisher_frame: *mut u64,
        context_pointers: *mut c_void,
    ) -> *mut c_void;
}

#[link(name = "dbghelp")]
extern "system" {
    fn SymInitializeW(process: Handle, search_path: *const u16, invade: i32) -> i32;
    fn SymSetSearchPathW(process: Handle, search_path: *const u16) -> i32;
    fn SymRefreshModuleList(process: Handle) -> i32;
    fn SymGetOptions() -> u32;
    fn SymSetOptions(options: u32) -> u32;
    fn SymFromAddrW(
        process: Handle,
        address: u64,
        displacement: *mut u64,
        symbol: *mut SymbolInfo,
    ) -> i32;
    fn SymGetLineFromAddrW64(
        process: Handle,
        address: u64,
        displacement: *mut u32,
        line: *mut LineInfo,
    ) -> i32;
    fn MiniDumpWriteDump(
        process: Handle,
        process_id: u32,
        file: Handle,
        dump_type: u32,
        exception: *const MinidumpExceptionInformation,
        user_stream: *const c_void,
        callback: *const c_void,
    ) -> i32;
}

static REQUEST: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static DONE: AtomicPtr<c_void> = AtomicPtr::new(null_mut());
static POINTERS: AtomicPtr<ExceptionPointers> = AtomicPtr::new(null_mut());
static THREAD_ID: AtomicU32 = AtomicU32::new(0);
static CRASHING: AtomicBool = AtomicBool::new(false);
static REPORTER_ID: AtomicU32 = AtomicU32::new(0);

pub(super) fn install() {
    // SAFETY: plain Win32 calls; the handles live for the rest of the process.
    unsafe {
        let request = CreateEventW(null_mut(), 0, 0, null());
        let done = CreateEventW(null_mut(), 1, 0, null());
        if request.is_null() || done.is_null() {
            return;
        }
        REQUEST.store(request, Ordering::SeqCst);
        DONE.store(done, Ordering::SeqCst);
    }
    let started = std::thread::Builder::new()
        .name("crash-reporter".into())
        .stack_size(8 << 20)
        .spawn(reporter);
    if started.is_ok() {
        // SAFETY: `filter` matches `LPTOP_LEVEL_EXCEPTION_FILTER`.
        unsafe { SetUnhandledExceptionFilter(Some(filter)) };
    }
}

/// Runs on the crashing thread: keep the stack use minimal.
unsafe extern "system" fn filter(pointers: *mut ExceptionPointers) -> i32 {
    if GetCurrentThreadId() == REPORTER_ID.load(Ordering::SeqCst) {
        // The reporter itself faulted (e.g. a corrupt stack): give up on the report.
        SetEvent(DONE.load(Ordering::SeqCst));
        return EXCEPTION_CONTINUE_SEARCH;
    }
    let done = DONE.load(Ordering::SeqCst);
    if CRASHING.swap(true, Ordering::SeqCst) {
        // Another thread is already being reported; the process is going down anyway.
        WaitForSingleObject(done, REPORT_TIMEOUT_MS);
        return EXCEPTION_CONTINUE_SEARCH;
    }
    POINTERS.store(pointers, Ordering::SeqCst);
    THREAD_ID.store(GetCurrentThreadId(), Ordering::SeqCst);
    SetEvent(REQUEST.load(Ordering::SeqCst));
    WaitForSingleObject(done, REPORT_TIMEOUT_MS);
    EXCEPTION_CONTINUE_SEARCH
}

fn reporter() {
    // SAFETY: the events were created in `install`; the exception pointers stay valid while
    // the crashing thread blocks in `filter`.
    unsafe {
        REPORTER_ID.store(GetCurrentThreadId(), Ordering::SeqCst);
        WaitForSingleObject(REQUEST.load(Ordering::SeqCst), INFINITE);
        let pointers = POINTERS.load(Ordering::SeqCst);
        let thread_id = THREAD_ID.load(Ordering::SeqCst);
        if !pointers.is_null() {
            report(pointers, thread_id);
        }
        SetEvent(DONE.load(Ordering::SeqCst));
    }
}

unsafe fn report(pointers: *mut ExceptionPointers, thread_id: u32) {
    let record = &*(*pointers).record;
    let context = *(*pointers).context;
    let thread = thread_name(thread_id).unwrap_or_else(|| "<unnamed>".into());

    let mut text = format!(
        "fatal exception 0x{:08X} ({}) at {:#x} on thread '{}' (id {})\n",
        record.code,
        exception_name(record.code),
        record.address as usize,
        thread,
        thread_id
    );
    if record.code == EXCEPTION_ACCESS_VIOLATION && record.number_parameters >= 2 {
        let op = match record.information[0] {
            0 => "reading",
            1 => "writing",
            8 => "executing",
            _ => "accessing",
        };
        let _ = writeln!(text, "{op} address {:#x}", record.information[1]);
    }
    // Write the header first: if symbolization hangs, the essentials are already on disk.
    super::append_report("FATAL", &text);

    let process = GetCurrentProcess();
    SymSetOptions(
        SymGetOptions()
            | SYMOPT_UNDNAME
            | SYMOPT_DEFERRED_LOADS
            | SYMOPT_LOAD_LINES
            | SYMOPT_FAIL_CRITICAL_ERRORS,
    );
    // The executable's directory (cargo puts the .pdb next to the .exe) + the default paths.
    // SymInitializeW fails harmlessly when std's backtrace support already initialized dbghelp.
    let mut search: Vec<u16> = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.as_os_str().to_owned()))
        .map(|d| {
            use std::os::windows::ffi::OsStrExt;
            d.encode_wide().collect()
        })
        .unwrap_or_default();
    search.extend(";.;".encode_utf16());
    if let Some(env) = std::env::var_os("_NT_SYMBOL_PATH") {
        use std::os::windows::ffi::OsStrExt;
        search.extend(env.encode_wide());
    }
    search.push(0);
    if SymInitializeW(process, search.as_ptr(), 1) == 0 {
        SymSetSearchPathW(process, search.as_ptr());
        SymRefreshModuleList(process);
    }

    let frames = walk(context);
    let _ = writeln!(text, "stack backtrace ({} frames):", frames.len());
    let print = |text: &mut String, range: std::ops::Range<usize>| {
        for i in range {
            // Return addresses point after the call: look up the call instruction.
            let pc = if i == 0 { frames[i].0 } else { frames[i].0 - 1 };
            let _ = writeln!(
                text,
                "  {i:>6}: {:#018x} {}",
                frames[i].0,
                symbolize(process, pc)
            );
        }
    };
    if frames.len() <= HEAD_FRAMES + TAIL_FRAMES {
        print(&mut text, 0..frames.len());
    } else {
        print(&mut text, 0..HEAD_FRAMES);
        let _ = writeln!(
            text,
            "  ... {} frames omitted ...",
            frames.len() - HEAD_FRAMES - TAIL_FRAMES
        );
        print(&mut text, frames.len() - TAIL_FRAMES..frames.len());

        // A stack overflow is usually a recursion: the functions of the cycle dominate.
        let mut counts: HashMap<u64, usize> = HashMap::new();
        for &(_, function) in &frames {
            *counts.entry(function).or_default() += 1;
        }
        let mut counts: Vec<(u64, usize)> = counts.into_iter().collect();
        counts.sort_by_key(|&(_, count)| std::cmp::Reverse(count));
        let _ = writeln!(text, "most frequent functions on the stack:");
        for (function, count) in counts.into_iter().take(SUMMARY_FUNCTIONS) {
            let _ = writeln!(text, "  {count:>6} x {}", symbolize(process, function));
        }
    }

    let dump = super::next_dump_path().and_then(|path| {
        let file = std::fs::File::create(&path).ok()?;
        let info = MinidumpExceptionInformation {
            thread_id,
            pointers,
            client_pointers: 0,
        };
        let ok = MiniDumpWriteDump(
            process,
            GetCurrentProcessId(),
            file.as_raw_handle() as Handle,
            MINIDUMP_WITH_UNLOADED_MODULES
                | MINIDUMP_WITH_INDIRECTLY_REFERENCED_MEMORY
                | MINIDUMP_WITH_THREAD_INFO,
            &info,
            null(),
            null(),
        );
        (ok != 0).then_some(path)
    });
    match dump {
        Some(path) => {
            let _ = writeln!(text, "minidump: {}", path.display());
        }
        None => text.push_str("minidump: not written\n"),
    }

    super::append_report("FATAL (stack)", &text);
    eprintln!("\n{text}");
}

/// Unwinds `context` frame by frame: `(pc, start address of its function)`.
unsafe fn walk(mut context: Context) -> Vec<(u64, u64)> {
    let mut frames = Vec::new();
    while frames.len() < MAX_FRAMES {
        let pc = context.reg(Context::RIP);
        if pc == 0 {
            break;
        }
        let sp = context.reg(Context::RSP);
        let mut image_base = 0u64;
        let function = RtlLookupFunctionEntry(pc, &mut image_base, null_mut());
        if function.is_null() {
            // Leaf function: the return address is on top of the stack.
            frames.push((pc, pc));
            context.set_reg(Context::RIP, *(sp as *const u64));
            context.set_reg(Context::RSP, sp + 8);
        } else {
            frames.push((pc, image_base + u64::from((*function).begin)));
            let mut handler_data = null_mut();
            let mut establisher = 0u64;
            RtlVirtualUnwind(
                0,
                image_base,
                pc,
                function,
                &mut context,
                &mut handler_data,
                &mut establisher,
                null_mut(),
            );
        }
        if context.reg(Context::RSP) <= sp {
            break;
        }
    }
    frames
}

unsafe fn symbolize(process: Handle, address: u64) -> String {
    let mut symbol: SymbolInfo = std::mem::zeroed();
    symbol.size_of_struct = 88;
    symbol.max_name_len = symbol.name.len() as u32;
    let mut displacement = 0u64;
    let mut out = if SymFromAddrW(process, address, &mut displacement, &mut symbol) != 0 {
        let len = (symbol.name_len as usize).min(symbol.name.len());
        format!(
            "{} + {:#x}",
            String::from_utf16_lossy(&symbol.name[..len]),
            displacement
        )
    } else {
        // No symbols: module + offset still lets the report be symbolized offline.
        module_offset(address)
    };
    let mut line: LineInfo = std::mem::zeroed();
    line.size_of_struct = std::mem::size_of::<LineInfo>() as u32;
    let mut line_displacement = 0u32;
    if SymGetLineFromAddrW64(process, address, &mut line_displacement, &mut line) != 0
        && !line.file_name.is_null()
    {
        let _ = write!(
            out,
            " ({}:{})",
            wide_to_string(line.file_name),
            line.line_number
        );
    }
    out
}

unsafe fn module_offset(address: u64) -> String {
    let mut module = null_mut();
    if GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        address as *const c_void,
        &mut module,
    ) == 0
    {
        return "<unknown>".into();
    }
    let mut name = [0u16; 512];
    let len = GetModuleFileNameW(module, name.as_mut_ptr(), name.len() as u32) as usize;
    let path = String::from_utf16_lossy(&name[..len.min(name.len())]);
    let file = path.rsplit('\\').next().unwrap_or(&path);
    format!("{file}+{:#x}", address - module as u64)
}

unsafe fn thread_name(thread_id: u32) -> Option<String> {
    let thread = OpenThread(THREAD_QUERY_LIMITED_INFORMATION, 0, thread_id);
    if thread.is_null() {
        return None;
    }
    let mut description = null_mut();
    let hr = GetThreadDescription(thread, &mut description);
    CloseHandle(thread);
    if hr < 0 || description.is_null() {
        return None;
    }
    let name = wide_to_string(description);
    LocalFree(description.cast());
    (!name.is_empty()).then_some(name)
}

unsafe fn wide_to_string(s: *const u16) -> String {
    let mut len = 0;
    while *s.add(len) != 0 {
        len += 1;
    }
    String::from_utf16_lossy(std::slice::from_raw_parts(s, len))
}

fn exception_name(code: u32) -> &'static str {
    match code {
        EXCEPTION_ACCESS_VIOLATION => "access violation",
        EXCEPTION_STACK_OVERFLOW => "stack overflow",
        0xC000_001D => "illegal instruction",
        0xC000_0094 => "integer divide by zero",
        0xC000_0095 => "integer overflow",
        0xC000_0006 => "in-page error",
        0xC000_008C => "array bounds exceeded",
        0xC000_0409 => "stack buffer overrun / fail-fast",
        0xC000_0374 => "heap corruption",
        0x8000_0003 => "breakpoint",
        _ => "unknown",
    }
}
