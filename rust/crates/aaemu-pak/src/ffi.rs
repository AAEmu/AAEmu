//! C bridge. Each context is shared safely by concurrent callers, but must not be closed
//! while a caller is using it. Errors belong to the calling thread.
use crate::{invalid, Pak, Result};
use std::cell::RefCell;
use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};

thread_local! { static LAST_ERROR: RefCell<String> = const { RefCell::new(String::new()) }; }
struct Context(Pak);

fn boundary<T>(fallback: T, operation: impl FnOnce() -> Result<T>) -> T {
    match catch_unwind(AssertUnwindSafe(operation)) {
        Ok(Ok(value)) => {
            LAST_ERROR.with(|error| error.borrow_mut().clear());
            value
        }
        Ok(Err(error)) => {
            LAST_ERROR.with(|last| *last.borrow_mut() = error.to_string().replace('\0', " "));
            fallback
        }
        Err(_) => {
            LAST_ERROR.with(|last| {
                *last.borrow_mut() = "panic contained by compressed pak bridge".into()
            });
            fallback
        }
    }
}

#[no_mangle]
pub extern "C" fn aaemu_pak_abi() -> u32 {
    1
}

/// Opens and pins an immutable manifest; returns null on error.
///
/// # Safety
/// `path` must reference `length` readable UTF-8 bytes for this call.
#[no_mangle]
pub unsafe extern "C" fn aaemu_pak_open(path: *const u8, length: u32) -> *mut c_void {
    boundary(std::ptr::null_mut(), || {
        if path.is_null() || length == 0 || length > 32768 {
            return Err(invalid("invalid manifest path pointer/length"));
        }
        let bytes = unsafe { std::slice::from_raw_parts(path, length as usize) };
        if bytes.contains(&0) {
            return Err(invalid("manifest path contains NUL"));
        }
        let path = std::str::from_utf8(bytes).map_err(|_| invalid("manifest path is not UTF-8"))?;
        let context = Box::new(Context(Pak::open(path)?));
        Ok(Box::into_raw(context).cast())
    })
}

/// Returns logical size, or -1 on error.
///
/// # Safety
/// A non-null `handle` must be an unclosed result of `aaemu_pak_open`.
#[no_mangle]
pub unsafe extern "C" fn aaemu_pak_size(handle: *mut c_void) -> i64 {
    boundary(-1, || {
        if handle.is_null() {
            return Err(invalid("null pak handle"));
        }
        let context = unsafe { &*handle.cast::<Context>() };
        Ok(context.0.manifest.logical_size as i64)
    })
}

/// Returns bytes read (0 at/past EOF), or MAXDWORD on error. No bytes beyond the returned
/// count are written on success. A failed multi-chunk read may have filled part of output.
///
/// # Safety
/// `handle` must be live, and `output` must reference `count` writable bytes when nonzero.
/// Do not close the handle concurrently with reads.
#[no_mangle]
pub unsafe extern "C" fn aaemu_pak_read_at(
    handle: *mut c_void,
    offset: u64,
    output: *mut c_void,
    count: u32,
) -> u32 {
    boundary(u32::MAX, || {
        if handle.is_null()
            || count == u32::MAX
            || count as u64 > isize::MAX as u64
            || (count != 0 && output.is_null())
        {
            return Err(invalid("invalid read handle/buffer/length"));
        }
        let context = unsafe { &*handle.cast::<Context>() };
        let buffer = if count == 0 {
            &mut []
        } else {
            unsafe { std::slice::from_raw_parts_mut(output.cast::<u8>(), count as usize) }
        };
        Ok(context.0.read_at(offset, buffer)? as u32)
    })
}

/// Closes a context. Null is accepted.
///
/// # Safety
/// A non-null handle must be live and exclusively owned for this call; close exactly once.
#[no_mangle]
pub unsafe extern "C" fn aaemu_pak_close(handle: *mut c_void) {
    boundary((), || {
        if !handle.is_null() {
            drop(unsafe { Box::from_raw(handle.cast::<Context>()) });
        }
        Ok(())
    });
}

/// Copies a NUL-terminated error prefix when capacity is nonzero; returns the full error
/// length excluding NUL, so callers can retry with a larger buffer. Does not clear error.
///
/// # Safety
/// A non-null output must reference `capacity` writable bytes. Null/zero queries are valid.
#[no_mangle]
pub unsafe extern "C" fn aaemu_pak_error(output: *mut u8, capacity: u32) -> u32 {
    catch_unwind(AssertUnwindSafe(|| {
        LAST_ERROR.with(|last| {
            let error = last.borrow();
            if !output.is_null() && capacity > 0 && capacity as u64 <= isize::MAX as u64 {
                let count = error.len().min(capacity as usize - 1);
                unsafe {
                    std::ptr::copy_nonoverlapping(error.as_ptr(), output, count);
                    *output.add(count) = 0;
                }
            }
            error.len().min(u32::MAX as usize) as u32
        })
    }))
    .unwrap_or(u32::MAX)
}

#[cfg(test)]
pub(crate) fn panic_probe() -> u32 {
    boundary(u32::MAX, || panic!("test bridge panic"))
}
