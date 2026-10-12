#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <vector>
#include "input.h"

namespace input {
#ifdef AADRIVER_INPUT_TEST
// One shared declaration lets the optimized fixture see getter/setter aliasing.
extern int physical;
extern bool modifier_get_supported,modifier_set_supported;
extern void* mouse_slots[29];
extern unsigned char mouse_module[0x5000];
extern bool mouse_locked_flag;
#endif
struct Key { const char* name; int id; int modifier; };
// r208022 keyboard key IDs (escape = 0, number row, tab, Q/W/E..., space = 56).
static const Key keys[] = {{"w",16,0},{"a",29,0},{"s",30,0},{"d",31,0},{"f",32,0},
    {"g",33,0},{"space",56,0},{"q",15,0},{"e",17,0},
    // r208022 keyboard constructors 335036BB..33503775: R through P -> IDs18..24.
    {"r",18,0},{"t",19,0},{"y",20,0},{"u",21,0},{"i",22,0},{"o",23,0},{"p",24,0},
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
    return modifier_get_supported?physical:0;
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
static bool modifier_state_supported(void* env) {
#ifdef AADRIVER_INPUT_TEST
    return env && modifier_get_supported && modifier_set_supported;
#else
    void* object=checked(env); if(!object) return false;
    BYTE* module=(BYTE*)GetModuleHandleA("CryInput.dll");
    void** vt=*(void***)object;
    // Exact r208022 vtable at RVA C388: GetModifiers slot22 / SetModifiers slot24.
    return vt[22]==module+0x1E65 && vt[24]==module+0x1E6D;
#endif
}
static void set_modifiers(void* env,int mask) {
#ifdef AADRIVER_INPUT_TEST
    (void)env;
    physical=mask;
#else
    void* object=checked(env);
    typedef void (__thiscall *Set)(void*,int);
    ((Set)(*(void***)object)[24])(object,mask);
#endif
}
struct ModifierShadow {
    void* env; int saved; bool active;
    ModifierShadow(void* e,int mask):env(e),saved(0),active(modifier_state_supported(e)) {
        if(active) { saved=physical_modifiers(env);set_modifiers(env,mask); }
    }
    ~ModifierShadow() { if(active) set_modifiers(env,saved); }
};
#ifndef AADRIVER_INPUT_TEST
struct EnabledInput {
    void* object; bool was_enabled;
    EnabledInput(void* o):object(o),was_enabled(*((BYTE*)o+36)!=0) {
        if(!was_enabled) enable(true);
    }
    ~EnabledInput() { if(!was_enabled) enable(false); }
    void enable(bool value) {
        typedef void (__thiscall *Enable)(void*,bool);
        ((Enable)(*(void***)object)[7])(object,value);
    }
};
#endif
static bool post(void* env,const Key& key,bool down,int mask) {
    Event event={0,down?1:2,key.name,key.id,0,mask,down?1.f:0.f,NULL};
    if(!checked(env)) return false;
    try {
        // Stock UI listeners can query GetModifiers synchronously instead of Event.modifiers.
        // Restore the exact native state before returning; no duration-wide modifier shadow.
        ModifierShadow shadow(env,mask);
#ifdef AADRIVER_INPUT_TEST
        extern bool record(const Event&);
        return record(event);
#else
        void* object=checked(env);
        typedef void (__thiscall *Post)(void*,const Event&);
        void** vt=*(void***)object;
        EnabledInput enabled(object);
        ((Post)vt[8])(object,event);
        return true;
#endif
    } catch(...) { return false; }
}
static int held_modifiers() {
    int mask=0;
    for(size_t i=0;i<held.size();++i) mask|=held[i].modifiers;
    return mask;
}
static bool mouse_slots_supported(void** slots,BYTE* module) {
    // CMessageInput vtable RVA C850, forwarding to the owned mouse device.
    return slots[26]==module+0x4564 && slots[28]==module+0x457C;
}
static bool checked_mouse(void* env) {
    if(!checked(env) || !modifier_state_supported(env)) return false;
#ifdef AADRIVER_INPUT_TEST
    return mouse_slots_supported(mouse_slots,mouse_module);
#else
    void* object=checked(env);
    BYTE* module=(BYTE*)GetModuleHandleA("CryInput.dll");
    if(!mouse_slots_supported(*(void***)object,module)) return false;
    void* device=*(void**)((BYTE*)object+0x68);
    return device && *((int*)((BYTE*)device+4))==1
        && (*(void***)device)[14]==module+0x49E7
        && (*(void***)device)[16]==module+0x4F7C;
#endif
}
static bool mouse_locked(void* env) {
#ifdef AADRIVER_INPUT_TEST
    (void)env;
    return mouse_locked_flag;
#else
    void* object=checked(env);
    typedef bool (__thiscall *Get)(void*);
    return ((Get)(*(void***)object)[28])(object);
#endif
}
static bool mouse_event(void* env,int id,int state,const char* name,float value) {
    if(!checked_mouse(env)) return false;
    Event event={1,state,name,id,0,physical_modifiers(env)|held_modifiers(),value,NULL};
    try {
#ifndef AADRIVER_INPUT_TEST
        void* object=checked(env);
        EnabledInput enabled(object);
#endif
        ModifierShadow shadow(env,event.modifiers);
#ifdef AADRIVER_INPUT_TEST
        extern bool record_mouse(const Event&);
        return record_mouse(event);
#else
        typedef void (__thiscall *Mouse)(void*,const Event&);
        ((Mouse)(*(void***)object)[26])(object,event);
        return true;
#endif
    } catch(...) { return false; }
}
static std::string mouse_control(void* env,const std::string& command,bool click) {
    char name[16]={0},extra=0;unsigned x=0,y=0;
    if(sscanf_s(command.c_str(),"%15s %u %u %c",name,(unsigned)sizeof(name),&x,&y,&extra,1)!=3
        || strcmp(name,click?"mouse_click":"mouse_move") || x>4095 || y>4095)
        return "2mouse input expects client-pixel x y within 0..4095";
    if(!checked_mouse(env)) return "2CryInput mouse is not the supported r208022 build";
    // The native locked-position path calls SetCursorPos; never enter that path.
    if(mouse_locked(env)) return "2mouse input unavailable while native mouse is locked";
    if(!mouse_event(env,269,8,"pos_x",(float)x)) return "2mouse input unavailable";
    // A synchronous listener can change mouse-lock state after the first axis.
    if(mouse_locked(env)) return "2mouse input became locked";
    if(!mouse_event(env,270,8,"pos_y",(float)y)) return "2mouse input unavailable";
    if(!click) return "0mouse moved (experimental native consumer validation required)";
    if(mouse_locked(env)) return "2mouse input became locked";
    bool pressed=mouse_event(env,256,1,"mouse1",1.f);
    // Mouse1 bypasses the position-lock branch, so cleanup remains safe if a click
    // listener locks the mouse. Always release after an attempted press, including errors.
    bool released=mouse_event(env,256,2,"mouse1",0.f);
    if(!released) mouse_event(env,256,2,"mouse1",0.f);
    return pressed && released?"0mouse clicked (experimental native consumer validation required)"
        :"2mouse click failed; button release attempted";
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
    if(command.compare(0,10,"mouse_move")==0) return mouse_control(env,command,false);
    if(command.compare(0,11,"mouse_click")==0) return mouse_control(env,command,true);
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
    if((mask|held_modifiers())!=0 && !modifier_state_supported(env))
        return "2CryInput modifier state is not the supported r208022 build";
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
