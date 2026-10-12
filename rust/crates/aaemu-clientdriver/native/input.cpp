#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <vector>
#include "input.h"

namespace input {
struct Key { const char* name; int id; };
// r208022 keyboard key IDs (escape = 0, number row, tab, Q/W/E..., space = 56).
static const Key keys[] = {{"w",16},{"a",29},{"s",30},{"d",31},{"f",32},
    {"g",33},{"space",56},{"q",15},{"e",17}};
struct Held { const Key* key; ULONGLONG until; };
static std::vector<Held> held;
// SInputEvent from CryInput.dll sub_3350439C: 32 bytes, value at +24.
struct Event { int device, state; const char* name; int id, unicode, modifiers;
    float value; void* symbol; };
static_assert(sizeof(Event)==32,"x86 SInputEvent");

static void* checked(void* env) {
#ifdef AADRIVER_INPUT_TEST
    return env;
#else
    BYTE* module=(BYTE*)GetModuleHandleA("CryInput.dll");
    if (!module || !env) return NULL;
    IMAGE_NT_HEADERS* nt=(IMAGE_NT_HEADERS*)(module+((IMAGE_DOS_HEADER*)module)->e_lfanew);
    if(nt->FileHeader.TimeDateStamp!=0x543CB7CE || nt->OptionalHeader.SizeOfImage!=0x14000) return NULL;
    void* object=*(void**)((BYTE*)env+16);
    if(!object || (*(void***)object)[8]!=module+0x14D6) return NULL;
    return object;
#endif
}
static bool post(void* env,const Key& key,bool down) {
#ifdef AADRIVER_INPUT_TEST
    extern bool record(const Key&,bool);
    return env && record(key,down);
#else
    void* object=checked(env); if(!object) return false;
    typedef void (__thiscall *Enable)(void*,bool);
    typedef void (__thiscall *Post)(void*,const Event&);
    void** vt=*(void***)object;
    bool enabled=*((BYTE*)object+36)!=0;
    if(!enabled) ((Enable)vt[7])(object,true);
    Event event={0,down?1:2,key.name,key.id,0,0,down?1.f:0.f,NULL};
    ((Post)vt[8])(object,event);
    if(!enabled) ((Enable)vt[7])(object,false);
    return true;
#endif
}
void release(void* env) {
    for(size_t i=0;i<held.size();++i) post(env,*held[i].key,false);
    held.clear();
}
static void tick_at(void* env,ULONGLONG now) {
    for(size_t i=0;i<held.size();) {
        if(now>=held[i].until) { post(env,*held[i].key,false); held.erase(held.begin()+i); }
        else ++i;
    }
}
void tick(void* env) { tick_at(env,GetTickCount64()); }
static std::string control_at(void* env,const std::string& command,ULONGLONG now) {
    if(command=="release") { release(env);return "0released"; }
    char name[16]={0},extra=0;unsigned duration=0;
    if(sscanf_s(command.c_str(),"%15s %u %c",name,(unsigned)sizeof(name),&duration,&extra,1)!=2
        || duration<1 || duration>10000) return "2input expects a supported key and duration 1..10000 ms";
    const Key* key=NULL;
    for(size_t i=0;i<sizeof(keys)/sizeof(keys[0]);++i) if(!strcmp(name,keys[i].name)) key=&keys[i];
    if(!key) return "2unsupported key";
    if(!checked(env)) return "2CryInput is not the supported r208022 build";
    for(size_t i=0;i<held.size();++i) if(held[i].key==key) return "2key already held; release first";
    if(!post(env,*key,true)) return "2input unavailable";
    held.push_back(Held{key,now+duration});
    return "0pressed (automatic release scheduled)";
}
std::string control(void* env,const std::string& command) { return control_at(env,command,GetTickCount64()); }
}
