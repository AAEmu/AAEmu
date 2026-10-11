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
static CRITICAL_SECTION g_lock;

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
struct Request {
    char        op;       // 'S' / 'U' / 'T' / 'B'
    char        target;   // for 'B'
    std::string code;
    std::string reply;    // status byte + text
    HANDLE      done;
};
static std::deque<Request*> g_queue;

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
                r->reply = r->op == 'B' ? runExecuteBuffer(r->target, r->code) : runLua(r->op, r->code);
            } };
            Run::go(r);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            r->reply = "2exception while running the request";
        }
        SetEvent(r->done);
    }
}

static void __fastcall HookUpdate(void* self, void* /*edx*/) {
    InterlockedIncrement(&g_inHook);
    g_origUpdate(self);
    if (!g_unloading) {
        if (!g_mainTid) g_mainTid = GetCurrentThreadId();
        if (GetCurrentThreadId() == g_mainTid) drainQueue();
    }
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
    for (int i = 0; i < 6000; ++i) {  // up to 10 minutes
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
    return buf;
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
    InterlockedExchange(&g_unloading, 1);
    removeHook();
    // let in-flight hook calls leave our code before the module goes away
    for (int i = 0; i < 200 && g_inHook; ++i) Sleep(10);
    Sleep(200);
    logf("unloading");
    FreeLibraryAndExitThread(g_self, 0);
    return 0;
}

static DWORD WINAPI serverThread(LPVOID) {
    if (!waitForClient() || !installHook()) { logf("driver inactive"); }
    char name[64];
    _snprintf_s(name, sizeof(name), _TRUNCATE, "\\\\.\\pipe\\aaemu-clientdriver-%lu", GetCurrentProcessId());
    logf("serving %s", name);
    for (;;) {
        HANDLE h = CreateNamedPipeA(name, PIPE_ACCESS_DUPLEX, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
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
            case 'S': case 'U': case 'T': reply = submit(op, 0, req.substr(1), 30000); break;
            case 'B':
                reply = req.size() < 2 ? std::string("2missing target") : submit('B', req[1], req.substr(2), 30000);
                break;
            case 'X': reply = "0unloading"; unload = true; break;
            default: reply = std::string("2unknown op '") + op + "'"; break;
            }
            if (!sendReply(h, reply) || unload) break;
        }
        FlushFileBuffers(h); DisconnectNamedPipe(h); CloseHandle(h);
        if (unload) { CreateThread(NULL, 0, unloadThread, NULL, 0, NULL); return 0; }
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = hModule;
        DisableThreadLibraryCalls(hModule);
        InitializeCriticalSection(&g_lock);
        GetModuleFileNameA(hModule, g_logPath, sizeof(g_logPath));
        char* slash = strrchr(g_logPath, '\\');
        if (slash) strcpy_s(slash + 1, g_logPath + sizeof(g_logPath) - (slash + 1), "aadriver.log");
        logf("aadriver loaded into pid %lu", GetCurrentProcessId());
        CreateThread(NULL, 0, serverThread, NULL, 0, NULL);
    }
    return TRUE;
}
