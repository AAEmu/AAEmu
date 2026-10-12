#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <vector>
#include "input.h"

namespace input {
struct Key { const char* name; int id; int modifier; };
// r208022 keyboard key IDs (escape = 0, number row, tab, Q/W/E..., space = 56).
static const Key keys[] = {{"w",16,0},{"a",29,0},{"s",30,0},{"d",31,0},{"f",32,0},
    {"g",33,0},{"space",56,0},{"q",15,0},{"e",17,0},
    {"1",1,0},{"2",2,0},{"3",3,0},{"4",4,0},{"5",5,0},
    {"6",6,0},{"7",7,0},{"8",8,0},{"9",9,0},{"0",10,0}};
// Exact r208022 keyboard symbols (3350381A / 335039B9 / 33503B9E):
// modifier flags include both sides of the native ctrl/shift/alt mask.
static const Key modifiers[] = {{"lctrl",28,0x11},{"shift",41,0x22},{"alt",55,0x44}};
static const char* modifier_names[] = {"ctrl","shift","alt"};
struct Held { const Key* key; int modifiers; ULONGLONG until; };
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
static int physical_modifiers(void* env) {
#ifdef AADRIVER_INPUT_TEST
    (void)env;
    extern int physical;
    return physical;
#else
    void* object=checked(env); if(!object) return 0;
    BYTE* module=(BYTE*)GetModuleHandleA("CryInput.dll");
    void** vt=*(void***)object;
    // GetModifiers, virtual slot22, reads Input+0x54 (33501E65).
    if(vt[22]!=module+0x1E65) return 0;
    typedef int (__thiscall *Get)(void*);
    return ((Get)vt[22])(object);
#endif
}
static bool post(void* env,const Key& key,bool down,int mask) {
    Event event={0,down?1:2,key.name,key.id,0,mask,down?1.f:0.f,NULL};
#ifdef AADRIVER_INPUT_TEST
    extern bool record(const Event&);
    return env && record(event);
#else
    void* object=checked(env); if(!object) return false;
    typedef void (__thiscall *Enable)(void*,bool);
    typedef void (__thiscall *Post)(void*,const Event&);
    void** vt=*(void***)object;
    bool enabled=*((BYTE*)object+36)!=0;
    if(!enabled) ((Enable)vt[7])(object,true);
    ((Post)vt[8])(object,event);
    if(!enabled) ((Enable)vt[7])(object,false);
    return true;
#endif
}
static int held_modifiers() {
    int mask=0;
    for(size_t i=0;i<held.size();++i) mask|=held[i].modifiers;
    return mask;
}
static void release_modifiers(void* env,int before,int after) {
    for(int i=2;i>=0;--i) {
        const Key& key=modifiers[i];
        if((before&key.modifier)!=0 && (after&key.modifier)==0) {
            before&=~key.modifier;
            post(env,key,false,physical_modifiers(env)|before);
        }
    }
}
void release(void* env) {
    int mask=held_modifiers();
    // All primary keys leave before any chord modifiers.
    for(size_t i=0;i<held.size();++i) post(env,*held[i].key,false,physical_modifiers(env)|mask);
    held.clear();
    release_modifiers(env,mask,0);
}
static void tick_at(void* env,ULONGLONG now) {
    for(size_t i=0;i<held.size();) {
        if(now>=held[i].until) {
            int mask=held_modifiers();
            post(env,*held[i].key,false,physical_modifiers(env)|mask);
            held.erase(held.begin()+i);
            // Shared modifiers stay pressed until their last chord ends.
            release_modifiers(env,mask,held_modifiers());
        }
        else ++i;
    }
}
void tick(void* env) { tick_at(env,GetTickCount64()); }
static std::string control_at(void* env,const std::string& command,ULONGLONG now) {
    if(command=="release") { release(env);return "0released"; }
    char name[32]={0},extra=0;unsigned duration=0;
    if(sscanf_s(command.c_str(),"%31s %u %c",name,(unsigned)sizeof(name),&duration,&extra,1)!=2
        || duration<1 || duration>10000) return "2input expects a supported key and duration 1..10000 ms";
    int mask=0;
    char* primary=name;
    while(char* plus=strchr(primary,'+')) {
        *plus=0;
        int part=0;
        for(size_t i=0;i<3;++i) if(!strcmp(primary,modifier_names[i])) part=modifiers[i].modifier;
        if(!part || (mask&part)!=0) return "2unsupported or duplicate modifier";
        mask|=part;
        primary=plus+1;
    }
    const Key* key=NULL;
    for(size_t i=0;i<sizeof(keys)/sizeof(keys[0]);++i) if(!strcmp(primary,keys[i].name)) key=&keys[i];
    if(!key) return "2unsupported key";
    if(!checked(env)) return "2CryInput is not the supported r208022 build";
    for(size_t i=0;i<held.size();++i) if(held[i].key==key) return "2key already held; release first";
    int before=held_modifiers(),down=before;
    for(size_t i=0;i<3;++i) {
        const Key& mod=modifiers[i];
        if((mask&mod.modifier)!=0 && (down&mod.modifier)==0) {
            down|=mod.modifier;
            if(!post(env,mod,true,physical_modifiers(env)|down)) {
                release_modifiers(env,down&~mod.modifier,before);
                return "2input unavailable";
            }
        }
    }
    if(!post(env,*key,true,physical_modifiers(env)|down)) {
        release_modifiers(env,down,before);
        return "2input unavailable";
    }
    held.push_back(Held{key,mask,now+duration});
    return "0pressed (automatic release scheduled)";
}
std::string control(void* env,const std::string& command) { return control_at(env,command,GetTickCount64()); }
}
