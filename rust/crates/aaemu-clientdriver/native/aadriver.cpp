// ============================================================================
//  aadriver.dll - in-client side of aaemu-clientdriver (ArcheAge 1.2, r208022, x86)
//
//  Loaded into a running archeage.exe (aaemu-clientdriver `inject`, i.e. LoadLibraryW
//  through CreateRemoteThread). Nothing in the client installation is modified on disk.
//
//  * waits for CryScriptSystem.dll and checks it is the known build (TimeDateStamp);
//  * replaces slot 3 (CScriptSystem::Update, called once per frame on the main thread)
//    of the CScriptSystem vtable *in memory* with a thunk that calls the original and
//    then runs queued requests on the main thread;
//  * serves the named pipe \\.\pipe\aaemu-clientdriver-<pid>; each request is run
//    against one of the engine's Lua states (System / UI) with the client's own Lua 5.1
//    API (luaL_loadbuffer + lua_pcall) and the result is sent back.
//
//  Wire format (both directions): u32 LE length, then `length` bytes.
//    request : op (1 byte) + payload
//      'S' <lua>   run in the System script state  (gEnv->pScriptSystem, lua_handle 0)
//      'U' <lua>   run in the UI script state      (lua_handle 1)
//      'T' <lua>   run in the third state (gEnv+40, lua_handle 2) when present
//      'B' <t><lua> run through the engine's own IScriptSystem::ExecuteBuffer
//                  (t = 'S'/'U'/'T'; no result value, only ok / failed)
//      'P'         ping: driver / client info
//      'X'         unhook and unload the driver
//    response: status (1 byte: '0' ok, '1' Lua error, '2' driver error) + UTF-8 text
//
//  Lua requests are compiled first as `return <code>` (expression) and, if that does not
//  compile, as `<code>` (statements), like the stand-alone Lua REPL. Return values are
//  serialised (strings raw at the top level, tables to depth 3) and joined with TAB.
// ============================================================================
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <string>
#include <deque>
#include "pakio.h"
#include "virtualpak.h"
#include "startup.h"
#ifndef AADRIVER_PLAYER
#include "visuals.h"
#include "input.h"
#include "audio.h"
#endif

// ---- client build constants (CryScriptSystem.dll, preferred base 0x35500000) -----------
// All addresses are RVAs; the DLL is relocated at run time (seen at 0x27E70000).
static const DWORD kCssTimeDateStamp = 0x543CB87C;  // CryScriptSystem.dll of 1.2 r208022
static const DWORD kCssSizeOfImage   = 0x000A2000;
static const DWORD RVA_gEnv          = 0x00081EF4;  // SSystemGlobalEnvironment* (set by CreateScriptSystem)
static const DWORD RVA_vtbl          = 0x000703D0;  // CScriptSystem vftable (IScriptSystem)
static const int   SLOT_Update       = 3;           // CScriptSystem::Update()        0x3550CA70
static const int   SLOT_ExecBuffer   = 6;           // ExecuteBuffer(buf, len, desc)  0x3550BB10
static const DWORD RVA_luaL_loadbuffer = 0x00048930;
static const DWORD RVA_lua_pcall       = 0x00047920;
static const DWORD RVA_lua_tolstring   = 0x00046F90;
static const DWORD RVA_lua_gettop      = 0x00046AE0;
static const DWORD RVA_lua_settop      = 0x00046B00;
static const int   OFF_SS_LuaState   = 16;  // CScriptSystem::m_pLS (vtable slot 58 returns it)
static const int   OFF_gEnv_SSystem  = 32;  // gEnv->pScriptSystem   (lua_handle 0, "System")
static const int   OFF_gEnv_SSUI     = 36;  // UI script system       (lua_handle 1, "UI")
static const int   OFF_gEnv_SSThird  = 40;  // third script system    (lua_handle 2)

typedef struct lua_State lua_State;
typedef int         (__cdecl *luaL_loadbuffer_t)(lua_State*, const char*, size_t, const char*);
typedef int         (__cdecl *lua_pcall_t)(lua_State*, int, int, int);
typedef const char* (__cdecl *lua_tolstring_t)(lua_State*, int, size_t*);
typedef int         (__cdecl *lua_gettop_t)(lua_State*);
typedef void        (__cdecl *lua_settop_t)(lua_State*, int);
typedef void        (__thiscall *Update_t)(void* self);
typedef bool        (__thiscall *ExecuteBuffer_t)(void* self, const char* buf, size_t len, const char* desc);

static luaL_loadbuffer_t p_loadbuffer;
static lua_pcall_t       p_pcall;
static lua_tolstring_t   p_tolstring;
static lua_gettop_t      p_gettop;
static lua_settop_t      p_settop;

static HMODULE   g_self;
static BYTE*     g_css;            // CryScriptSystem.dll base
static void**    g_vtbl;           // in-memory vtable
static Update_t  g_origUpdate;
static volatile LONG g_hooked;
static volatile LONG g_inHook;
static volatile LONG g_unloading;
static DWORD     g_mainTid;
static char      g_logPath[MAX_PATH];
static bool g_fastStart;
static bool g_fastApplied;
static bool g_fastCVarApplied;
static std::string g_auctionUiScript;
static bool g_auctionUiApplied;
static bool g_auctionUiFailed;
static ULONGLONG g_auctionUiNextCheck;
#ifndef AADRIVER_PLAYER
static CRITICAL_SECTION g_lock;
static bool g_hideOnStart;
static bool g_silent;
static bool g_sound;
#endif

// ---- logging ---------------------------------------------------------------------------
static void logf(const char* fmt, ...) {
    char buf[1024];
    va_list ap; va_start(ap, fmt);
    _vsnprintf_s(buf, sizeof(buf), _TRUNCATE, fmt, ap);
    va_end(ap);
    HANDLE h = CreateFileA(g_logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h != INVALID_HANDLE_VALUE) {
        SYSTEMTIME st; GetLocalTime(&st);
        char line[1100];
        int n = _snprintf_s(line, sizeof(line), _TRUNCATE, "%02d:%02d:%02d.%03d [%lu] %s\r\n",
                            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, GetCurrentProcessId(), buf);
        DWORD w; WriteFile(h, line, n > 0 ? (DWORD)n : 0, &w, NULL);
        CloseHandle(h);
    }
    OutputDebugStringA("[aadriver] "); OutputDebugStringA(buf); OutputDebugStringA("\n");
}

// ---- request queue (pipe thread -> main thread) -----------------------------------------
#ifndef AADRIVER_PLAYER
struct Request {
    char        op;       // 'S' / 'U' / 'T' / 'B'
    char        target;   // for 'B'
    std::string code;
    std::string reply;    // status byte + text
    HANDLE      done;
};
static std::deque<Request*> g_queue;
#endif

// ---- Lua execution (main thread only) ---------------------------------------------------
static const char* kPrelude =
    "local __tostring, __type, __pairs, __select, __pcall = tostring, type, pairs, select, pcall\n"
    "local __fmt = string and string.format\n"
    "local __concat = table and table.concat\n"
    "local function __ser(v, d)\n"
    "  local t = __type(v)\n"
    "  if t == 'string' then if __fmt then return __fmt('%q', v) end return '\"' .. v .. '\"' end\n"
    "  if t ~= 'table' or d >= 3 or not __concat then return __tostring(v) end\n"
    "  local parts, n = {}, 0\n"
    "  for k, x in __pairs(v) do\n"
    "    n = n + 1\n"
    "    if n > 400 then parts[n] = '...'; break end\n"
    "    parts[n] = '[' .. __ser(k, 9) .. ']=' .. __ser(x, d + 1)\n"
    "  end\n"
    "  return '{' .. __concat(parts, ', ') .. '}'\n"
    "end\n"
    "local function __pack(ok, ...)\n"
    "  if not ok then return '\\1' .. __tostring((...)) end\n"
    "  local n, s = __select('#', ...), '\\0'\n"
    "  for i = 1, n do\n"
    "    local v = (__select(i, ...))\n"
    "    if i > 1 then s = s .. '\\t' end\n"
    "    if __type(v) == 'string' then s = s .. v else s = s .. __ser(v, 0) end\n"
    "  end\n"
    "  return s\n"
    "end\n"
    "return __pack(__pcall(function(...) ";

static void* scriptSystemFor(char target) {
    BYTE* env = *(BYTE**)(g_css + RVA_gEnv);
    if (!env) return NULL;
    int off = target == 'U' ? OFF_gEnv_SSUI : target == 'T' ? OFF_gEnv_SSThird : OFF_gEnv_SSystem;
    void* ss = *(void**)(env + off);
    if (!ss || *(void***)ss != g_vtbl) return NULL;  // must be a CScriptSystem
    return ss;
}

static std::string runLua(char target, const std::string& code) {
    void* ss = scriptSystemFor(target);
    if (!ss) return std::string("2script system '") + target + "' not available";
    lua_State* L = *(lua_State**)((BYTE*)ss + OFF_SS_LuaState);
    if (!L) return "2script system has no lua_State";
    int top = p_gettop(L);
    std::string body = "return " + code;
    std::string src = std::string(kPrelude) + body + "\nend))";
    int rc = p_loadbuffer(L, src.data(), src.size(), "=aadriver");
    if (rc != 0) {
        p_settop(L, top);
        src = std::string(kPrelude) + code + "\nend))";
        rc = p_loadbuffer(L, src.data(), src.size(), "=aadriver");
        if (rc != 0) {
            size_t n = 0; const char* e = p_tolstring(L, -1, &n);
            std::string err = std::string("1compile: ") + (e ? std::string(e, n) : "?");
            p_settop(L, top);
            return err;
        }
    }
    rc = p_pcall(L, 0, 1, 0);
    size_t n = 0; const char* s = p_tolstring(L, -1, &n);
    std::string out;
    if (rc != 0) out = std::string("1runtime: ") + (s ? std::string(s, n) : "?");
    else if (!s || n == 0) out = "2no result";
    else if (s[0] == '\1') out = "1" + std::string(s + 1, n - 1);
    else out = "0" + std::string(s + 1, n - 1);
    p_settop(L, top);
    return out;
}

#ifndef AADRIVER_PLAYER
static std::string runExecuteBuffer(char target, const std::string& code) {
    void* ss = scriptSystemFor(target);
    if (!ss) return std::string("2script system '") + target + "' not available";
    ExecuteBuffer_t fn = (ExecuteBuffer_t)g_vtbl[SLOT_ExecBuffer];
    bool ok = fn(ss, code.data(), code.size(), "aadriver");
    return ok ? "0ExecuteBuffer ok" : "1ExecuteBuffer failed (see ArcheAge.log)";
}

static void drainQueue() {
    for (;;) {
        Request* r = NULL;
        EnterCriticalSection(&g_lock);
        if (!g_queue.empty()) { r = g_queue.front(); g_queue.pop_front(); }
        LeaveCriticalSection(&g_lock);
        if (!r) return;
        __try {
            // (std::string work cannot sit directly in a __try frame with unwinding; delegate)
            struct Run { static void go(Request* r) {
                if (r->op == 'F') r->reply = pakio::verify();
                else if (r->op == 'I') r->reply = input::control(*(void**)(g_css + RVA_gEnv), r->code);
                else if (r->op == 'W') r->reply = visuals::window(r->code == "show");
                else r->reply = r->op == 'B' ? runExecuteBuffer(r->target, r->code) : runLua(r->op, r->code);
            } };
            Run::go(r);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            r->reply = "2exception while running the request";
        }
        SetEvent(r->done);
    }
}
#endif

static bool setSlot(void** slot, void* value);

static void __fastcall HookUpdate(void* self, void* /*edx*/) {
    InterlockedIncrement(&g_inHook);
    if (!g_unloading) {
        if (!g_mainTid) g_mainTid = GetCurrentThreadId();
        if (GetCurrentThreadId() == g_mainTid) {
#ifndef AADRIVER_PLAYER
            if (g_silent) runLua('S',
                "if System and System.GetCVar and System.SetCVar then "
                "local a,b=System.GetCVar('s_SoundEnable'),System.GetCVar('s_MusicEnable') "
                "if a~=nil and a~=0 then System.SetCVar('s_SoundEnable',0) end "
                "if b~=nil and b~=0 then System.SetCVar('s_MusicEnable',0) end end");
#endif
            // The game registers this CVar after command-line processing. Apply it at the
            // first script tick, on the Lua owner thread, before the logo stages advance.
            if (g_fastStart && !g_fastCVarApplied) {
                std::string reply = runLua('S',
                    "if System and (System.GetCVar('login_fast_start')) ~= nil then "
                    "System.SetCVar('login_fast_start',1) return (System.GetCVar('login_fast_start')) end");
                if (reply == "01") { g_fastCVarApplied = true; logf("fast start: login_fast_start=1"); }
            }
            if (g_fastStart && !g_fastApplied) {
                // r208022 chooses the initial logo state before the first script Update.
                // Use the same UI binding as the login transition once that state exists.
                std::string reply = runLua('U',
                    "if X2LoginCharacter and X2LoginCharacter.GetCurrentStage then "
                    "local stage=X2LoginCharacter:GetCurrentStage() "
                    "if stage==1 then X2LoginCharacter:StartLoginPage() return 'skipped' "
                    "elseif stage>=2 then return 'ready' end end");
                if (reply == "0skipped" || reply == "0ready") {
                    g_fastApplied = true; logf("fast start: login logo transition %s", reply.c_str() + 1);
                }
            }
#ifndef AADRIVER_PLAYER
            if (g_hideOnStart) {
                if (visuals::window(false)[0] == '0') { g_hideOnStart = false; logf("hidden window ready"); }
            }
#endif
        }
    }
    g_origUpdate(self);
    if (!g_unloading && GetCurrentThreadId() == g_mainTid &&
        self == scriptSystemFor('U') && !g_auctionUiFailed) {
        ULONGLONG now = GetTickCount64();
        if (now >= g_auctionUiNextCheck) {
            g_auctionUiNextCheck = now + 250;
            // Login/world transitions can clear globals in the same script system.
            // Check the actual installed functions, rather than a native one-shot flag.
            std::string state = runLua('U',
                "local p=AAEmuAuctionUiInstalled return type(p)=='table' and "
                "p.apply==ApplyButtonSkin and p.tooltip==SetTooltip and p.hide==HideTooltip");
            if (state != "0true") {
                g_auctionUiApplied = false;
                std::string reply = runLua('U', g_auctionUiScript);
                if (reply == "0ready") {
                    g_auctionUiApplied = true;
                    logf("auction UI: gavel artwork and English tooltip installed");
                } else if (!reply.empty() && reply[0] == '1') {
                    g_auctionUiFailed = true;
                    logf("auction UI unavailable: %s", reply.c_str() + 1);
                }
            }
        }
    }
#ifndef AADRIVER_PLAYER
    if (!g_unloading && GetCurrentThreadId() == g_mainTid) {
        // Profile/UI scripts can reset audio during Update. The process session
        // is already muted before startup; keep engine telemetry consistent too.
        if (g_silent) runLua('S',
            "if System and System.GetCVar and System.SetCVar then "
                "local a,b=System.GetCVar('s_SoundEnable'),System.GetCVar('s_MusicEnable') "
                "if a~=nil and a~=0 then System.SetCVar('s_SoundEnable',0) end "
                "if b~=nil and b~=0 then System.SetCVar('s_MusicEnable',0) end end");
        input::tick(*(void**)(g_css + RVA_gEnv));
        drainQueue();
    }
#endif
    InterlockedDecrement(&g_inHook);
}

// ---- hooking ------------------------------------------------------------------------------
static bool setSlot(void** slot, void* value) {
    DWORD old;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &old)) return false;
    InterlockedExchangePointer(slot, value);
    VirtualProtect(slot, sizeof(void*), old, &old);
    return true;
}

static bool waitForClient() {
    bool smooth = wcsstr(GetCommandLineW(), L"-aaemu-smooth-start") != NULL;
#ifndef AADRIVER_PLAYER
    bool multiClient = wcsstr(GetCommandLineW(), L"-aaemu-multi-client") != NULL;
#endif
    bool applied = false;
    for (int i = 0; i < 6000; ++i) {  // up to 10 minutes
#ifndef AADRIVER_PLAYER
        // The loader explicitly offers Yes/No to a second instance. Accept only this
        // process's known r208022 dialog when the launcher requested multiplayer.
        // No mutex, executable bytes or client files are changed.
        if (multiClient) EnumWindows([](HWND window, LPARAM) -> BOOL {
            DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
            if (pid != GetCurrentProcessId()) return TRUE;
            wchar_t title[64] = {}, klass[32] = {};
            GetWindowTextW(window, title, 64); GetClassNameW(window, klass, 32);
            if (wcscmp(title, L"Too many applications") || wcscmp(klass, L"#32770")) return TRUE;
            BYTE* exe = (BYTE*)GetModuleHandleW(NULL);
            IMAGE_NT_HEADERS* nt = (IMAGE_NT_HEADERS*)(exe + ((IMAGE_DOS_HEADER*)exe)->e_lfanew);
            if (nt->FileHeader.TimeDateStamp != 0x543CB762 || nt->OptionalHeader.SizeOfImage != 0x3E3C00) return TRUE;
            // Original loader RVA 11040 calls MessageBoxA(MB_YESNO) on a duplicate
            // ArcheAge mutex and continues on IDYES. Validate its prompt and buttons.
            wchar_t prompt[256] = {};
            GetDlgItemTextW(window, 0xffff, prompt, 256);
            HWND yes = GetDlgItem(window, IDYES), no = GetDlgItem(window, IDNO);
            if (!yes || !no || !wcsstr(prompt, L"There is already a x2client application running") ||
                !wcsstr(prompt, L"Do you want to start another one?")) return TRUE;
            PostMessageW(window, WM_COMMAND, MAKEWPARAM(IDYES, BN_CLICKED), (LPARAM)yes);
            logf("multi-client: accepted this process's additional-instance confirmation");
            return FALSE;
        }, 0);
#endif
        // Clock calibration runs before the script environment exists. Apply its
        // narrow priority fix as soon as CrySystem is loaded, ahead of Lua hooks.
        if (smooth && !applied && startup::install()) {
            applied = true; logf("smooth start: CPU clock calibration uses normal priorities");
        }
        HMODULE m = GetModuleHandleA("CryScriptSystem.dll");
        if (m) {
            BYTE* b = (BYTE*)m;
            IMAGE_NT_HEADERS* nt = (IMAGE_NT_HEADERS*)(b + ((IMAGE_DOS_HEADER*)b)->e_lfanew);
            if (nt->FileHeader.TimeDateStamp != kCssTimeDateStamp ||
                nt->OptionalHeader.SizeOfImage != kCssSizeOfImage) {
                logf("CryScriptSystem.dll is not the 1.2 r208022 build (stamp %08lX size %08lX) - not hooking",
                     nt->FileHeader.TimeDateStamp, nt->OptionalHeader.SizeOfImage);
                return false;
            }
            g_css = b;
            if (*(void**)(b + RVA_gEnv)) return true;
        }
        Sleep(100);
    }
    logf("timed out waiting for CryScriptSystem.dll / gEnv");
    return false;
}

static bool installHook() {
    HRSRC resource = FindResourceW(g_self, MAKEINTRESOURCEW(201), MAKEINTRESOURCEW(10));
    HGLOBAL loaded = resource ? LoadResource(g_self, resource) : NULL;
    const char* script = loaded ? static_cast<const char*>(LockResource(loaded)) : NULL;
    DWORD scriptSize = resource ? SizeofResource(g_self, resource) : 0;
    if (!script || !scriptSize) { logf("auction UI script resource missing"); return false; }
    g_auctionUiScript.assign(script, scriptSize);
    g_vtbl = (void**)(g_css + RVA_vtbl);
    p_loadbuffer = (luaL_loadbuffer_t)(g_css + RVA_luaL_loadbuffer);
    p_pcall      = (lua_pcall_t)(g_css + RVA_lua_pcall);
    p_tolstring  = (lua_tolstring_t)(g_css + RVA_lua_tolstring);
    p_gettop     = (lua_gettop_t)(g_css + RVA_lua_gettop);
    p_settop     = (lua_settop_t)(g_css + RVA_lua_settop);
    void* cur = g_vtbl[SLOT_Update];
    if (cur == (void*)&HookUpdate) return true;
    if (cur != (void*)(g_css + 0xCA70)) {
        logf("vtable slot %d = %p, expected CScriptSystem::Update %p - not hooking", SLOT_Update, cur, g_css + 0xCA70);
        return false;
    }
    g_origUpdate = (Update_t)cur;
    if (!setSlot(&g_vtbl[SLOT_Update], (void*)&HookUpdate)) { logf("VirtualProtect failed (%lu)", GetLastError()); return false; }
    InterlockedExchange(&g_hooked, 1);
    logf("hooked CScriptSystem::Update (vtable %p, CryScriptSystem.dll at %p)", g_vtbl, g_css);
    return true;
}

#ifndef AADRIVER_PLAYER
static void removeHook() {
    if (InterlockedExchange(&g_hooked, 0)) {
        setSlot(&g_vtbl[SLOT_Update], (void*)g_origUpdate);
        logf("unhooked");
    }
}

// ---- pipe server ----------------------------------------------------------------------------
static bool readAll(HANDLE h, void* buf, DWORD n) {
    BYTE* p = (BYTE*)buf;
    while (n) { DWORD r = 0; if (!ReadFile(h, p, n, &r, NULL) || r == 0) return false; p += r; n -= r; }
    return true;
}
static bool writeAll(HANDLE h, const void* buf, DWORD n) {
    const BYTE* p = (const BYTE*)buf;
    while (n) { DWORD w = 0; if (!WriteFile(h, p, n, &w, NULL) || w == 0) return false; p += w; n -= w; }
    return true;
}
static bool sendReply(HANDLE h, const std::string& s) {
    DWORD n = (DWORD)s.size();
    return writeAll(h, &n, 4) && writeAll(h, s.data(), n);
}

static std::string pingInfo() {
    char buf[512];
    BYTE* env = g_css ? *(BYTE**)(g_css + RVA_gEnv) : NULL;
    _snprintf_s(buf, sizeof(buf), _TRUNCATE,
                "0aadriver 1 pid=%lu css=%p gEnv=%p system=%p ui=%p third=%p hooked=%ld mainTid=%lu",
                GetCurrentProcessId(), g_css, env,
                env ? *(void**)(env + OFF_gEnv_SSystem) : NULL,
                env ? *(void**)(env + OFF_gEnv_SSUI) : NULL,
                env ? *(void**)(env + OFF_gEnv_SSThird) : NULL, g_hooked, g_mainTid);
    return std::string(buf) + visuals::info() + startup::info() + virtualpak::info() +
        " fast=" + (g_fastApplied ? "1" : "0") + " silent=" + (g_silent ? "1" : "0") +
        " auction_ui=" + (g_auctionUiApplied ? "1" : "0");
}

static std::string submit(char op, char target, const std::string& code, DWORD timeoutMs) {
    if (!g_hooked) return "2driver is not hooked";
    Request* r = new Request();
    r->op = op; r->target = target; r->code = code;
    r->done = CreateEventA(NULL, TRUE, FALSE, NULL);
    EnterCriticalSection(&g_lock); g_queue.push_back(r); LeaveCriticalSection(&g_lock);
    if (WaitForSingleObject(r->done, timeoutMs) != WAIT_OBJECT_0) {
        // Still queued: take it out again. If it is being run right now, wait for it.
        bool removed = false;
        EnterCriticalSection(&g_lock);
        for (std::deque<Request*>::iterator it = g_queue.begin(); it != g_queue.end(); ++it)
            if (*it == r) { g_queue.erase(it); removed = true; break; }
        LeaveCriticalSection(&g_lock);
        if (!removed) WaitForSingleObject(r->done, INFINITE);
        else { CloseHandle(r->done); delete r; return "2timeout: the client did not run a frame (minimised / loading?)"; }
    }
    std::string reply = r->reply;
    CloseHandle(r->done); delete r;
    return reply;
}

static DWORD WINAPI unloadThread(LPVOID) {
    submit('I', 0, "release", 30000);
    InterlockedExchange(&g_unloading, 1);
    removeHook();
    visuals::remove();
    pakio::remove();
    startup::remove();
    // let in-flight hook calls leave our code before the module goes away
    for (int i = 0; i < 200 && g_inHook; ++i) Sleep(10);
    Sleep(200);
    logf("detached (module retained until process exit for in-flight virtual calls)");
    return 0;
}

static DWORD WINAPI audioThread(LPVOID) {
        if (!audio::prepare_start(g_silent)) {
            logf("audio start: process audio session policy failed; launcher must not resume client");
            return 1;
        }
        if (!virtualpak::prepare_start()) {
            logf("compressed pak startup failed: %s; launcher must not resume client", virtualpak::error().c_str());
            return 1;
        }
        logf("pak startup:%s", virtualpak::info().c_str());
        wchar_t name[96];
        _snwprintf_s(name, _countof(name), _TRUNCATE, L"Local\\aaemu-client-audio-ready-%lu", GetCurrentProcessId());
        HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, name);
        if (!ready || !SetEvent(ready)) { logf("silent start: readiness event failed"); return 1; }
        // Keep the readiness event and COM registrations alive until process exit.
        logf("audio start: own process sessions %s before resume", g_silent ? "muted" : "unmuted once");
        // Session notifications need a live MTA apartment. This policy outlives
        // pipe disconnect and driver detach; the DLL is retained until exit.
        Sleep(INFINITE);
        return 0;
}

static DWORD WINAPI serverThread(LPVOID) {
    if (g_silent || g_sound) {
        HANDLE thread = CreateThread(nullptr,0,audioThread,nullptr,0,nullptr);
        if (!thread) { logf("audio start: policy thread failed"); return 1; }
        CloseHandle(thread);
    }
    if (!waitForClient() || !installHook()) { logf("driver inactive"); }
    else {
        if (wcsstr(GetCommandLineW(), L"-aaemu-fast-pak"))
            logf("fast pak: %s", pakio::control("optimize").c_str());
        logf("DXGI capture hook: %s", visuals::install() ? "ready" : "unavailable");
    }
    char name[64];
    _snprintf_s(name, sizeof(name), _TRUNCATE, "\\\\.\\pipe\\aaemu-clientdriver-%lu", GetCurrentProcessId());
    logf("serving %s", name);
    for (;;) {
        HANDLE h = CreateNamedPipeA(name, PIPE_ACCESS_DUPLEX, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                                    1, 1 << 16, 1 << 16, 0, NULL);
        if (h == INVALID_HANDLE_VALUE) { logf("CreateNamedPipe failed (%lu)", GetLastError()); return 1; }
        if (!ConnectNamedPipe(h, NULL) && GetLastError() != ERROR_PIPE_CONNECTED) { CloseHandle(h); continue; }
        bool unload = false;
        for (;;) {
            DWORD len = 0;
            if (!readAll(h, &len, 4) || len == 0 || len > (64u << 20)) break;
            std::string req(len, '\0');
            if (!readAll(h, &req[0], len)) break;
            char op = req[0];
            std::string reply;
            switch (op) {
            case 'P': reply = pingInfo(); break;
            case 'I': reply = submit('I', 0, req.substr(1), 30000); break;
            case 'V': reply = visuals::profile(req.substr(1)); break;
            case 'S': case 'U': case 'T': reply = submit(op, 0, req.substr(1), 30000); break;
            case 'B':
                reply = req.size() < 2 ? std::string("2missing target") : submit('B', req[1], req.substr(2), 30000);
                break;
            case 'C': reply = visuals::capture(); break;
            case 'F':
                reply = req.substr(1) == "verify" ? submit('F', 0, "", 30000) : pakio::control(req.substr(1));
                break;
            case 'W':
                reply = req.substr(1) == "hide" || req.substr(1) == "show"
                    ? submit('W', 0, req.substr(1), 30000) : "2window expects hide or show";
                break;
            case 'X': reply = "0detaching (module retained; restart client to reattach)"; unload = true; break;
            default: reply = std::string("2unknown op '") + op + "'"; break;
            }
            if (!sendReply(h, reply) || unload) break;
        }
        FlushFileBuffers(h); DisconnectNamedPipe(h); CloseHandle(h);
        if (unload) { CreateThread(NULL, 0, unloadThread, NULL, 0, NULL); return 0; }
    }
}
#else
// Player build: pack mounting, startup optimizations and auction UI repair. No command pipe,
// input automation, capture hooks, hidden window handling or audio session policy.
static DWORD WINAPI serverThread(LPVOID) {
    if (!virtualpak::prepare_start()) {
        logf("compressed pak startup failed: %s", virtualpak::error().c_str());
        return 1;
    }
    if (wcsstr(GetCommandLineW(), L"-aaemu-smooth-start") && !startup::install()) {
        logf("unsupported CPU calibration instructions; refusing player startup");
        return 1;
    }
    if (wcsstr(GetCommandLineW(), L"-aaemu-fast-pak")) {
        std::string result = pakio::control("optimize");
        if (result.empty() || result[0] != '0') { logf("fast pak: %s", result.c_str()); return 1; }
        logf("fast pak: %s", result.c_str());
    }
    logf("player startup:%s%s; normal client audio", virtualpak::info().c_str(), startup::info().c_str());
    // Reuse the launcher's startup-readiness handshake. The player build has no
    // audio policy; this event confirms the compressed pack is ready before resume.
    wchar_t name[96];
    _snwprintf_s(name, _countof(name), _TRUNCATE, L"Local\\aaemu-client-audio-ready-%lu", GetCurrentProcessId());
    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, name);
    if (!ready || !SetEvent(ready)) { logf("player readiness event failed"); return 1; }
    // Keep the event open until process exit, as in the diagnostic build.
    if (!waitForClient() || !installHook()) { logf("player startup/UI hook unavailable"); return 1; }
    return 0;
}
#endif

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = hModule;
        g_fastStart = wcsstr(GetCommandLineW(), L"-aaemu-fast-start") != NULL;
#ifndef AADRIVER_PLAYER
        g_hideOnStart = wcsstr(GetCommandLineW(), L"-aaemu-hidden") != NULL;
        g_silent = wcsstr(GetCommandLineW(), L"-aaemu-silent") != NULL;
        g_sound = wcsstr(GetCommandLineW(), L"-aaemu-sound") != NULL;
#endif
        DisableThreadLibraryCalls(hModule);
#ifndef AADRIVER_PLAYER
        InitializeCriticalSection(&g_lock);
#endif
        GetModuleFileNameA(hModule, g_logPath, sizeof(g_logPath));
        char* slash = strrchr(g_logPath, '\\');
        if (slash) strcpy_s(slash + 1, g_logPath + sizeof(g_logPath) - (slash + 1), "aadriver.log");
#ifdef AADRIVER_PLAYER
        logf("player runtime loaded into pid %lu", GetCurrentProcessId());
#else
        logf("aadriver loaded into pid %lu", GetCurrentProcessId());
#endif
        CreateThread(NULL, 0, serverThread, NULL, 0, NULL);
    }
    return TRUE;
}
