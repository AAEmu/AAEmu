/* physoracle: scenario-driven oracle for the ArcheAge 10.x zone-server physics.
 *
 * Loads the real x64 `cryphysics_dedicate.dll` (10.0.2.13) standalone, drives it through its
 * vtables and prints a bit-exact trace (every float as its raw IEEE-754 bits). The Rust port
 * (`aaemu-physics`) diffs against these traces.
 *
 *   physoracle.exe [--rt DIR] SCENARIO.txt|-        run a scenario, trace on stdout
 *   physoracle.exe [--rt DIR] --math NAME COUNT SEED   probe an internal math routine
 *
 * DIR defaults to %AAEMU_PHYS_RT% or D:\aa-re\cryphysics\harness\rt. It must hold
 * cryphysics_dedicate.dll, MSVCR100.dll, MSVCP100.dll, tbb.dll, xldiag.dll, xlcommon.dll (the
 * *original* xlcommon, not the r575 forwarding proxy) and the tbbmalloc_proxy stub (tbbstub.c).
 *
 * Every slot number, struct offset and internal address used here comes from the RE notes in
 * rust/docs/physics/ (derived from the shipped binary only). Scenario syntax: see README.md of
 * the aaemu-physics crate ("Oracle").
 *
 * Single-threaded, deterministic: two runs of the same scenario give identical output. Pointers
 * are never printed (entities are printed by scenario name).
 */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <io.h>
#include <fcntl.h>

#define IMAGE_BASE 0x35000000ULL
#define VA(h, va) ((void *)((char *)(h) + ((va) - IMAGE_BASE)))
#define VT(obj) (*(void ***)(obj))

/* ---- IPhysicalWorld slots (x64 10.x, CRYPHYSICS-MAP.md section 4) ---- */
enum {
  W_GetGeomManager = 9, W_SetupEntityGrid = 11, W_GetPhysVars = 20, W_CreatePhysicalEntity = 21,
  W_DestroyPhysicalEntity = 24, W_TimeStep = 32, W_RayWorldIntersection = 42, W_AddGlobalArea = 80,
  W_SetHeightfieldData = 17
};
/* ---- IGeomManager slots ---- */
enum { G_CreatePrimitive = 7, G_RegisterGeometry = 9 };
/* ---- IPhysicalEntity slots ---- */
enum { E_SetParams = 7, E_GetParams = 8, E_GetStatus = 9, E_Action = 10, E_AddGeometry = 11 };

#define UNUSED_F_BITS 0xFFBFFFFFu
#define UNUSED_I 0x80000000u

typedef void *(*fn_p)(void *);
typedef void (*fn_grid)(void *, int, float *, int, int, float, float, int, int);
typedef void *(*fn_create)(void *, int, float, void *, void *, int, int, void *, unsigned);
typedef int (*fn_timestep)(void *, float, int);
typedef void *(*fn_prim)(void *, int, void *);
typedef void *(*fn_reg)(void *, void *, int, int *, int);
typedef int (*fn_setp)(void *, void *, int);
typedef int (*fn_stat)(void *, void *);
typedef int (*fn_addg)(void *, void *, void *, int, int);
typedef int (*fn_rwi)(void *, void *, const char *, int);
typedef void (*fn_hfdata)(void *, void *, int *, int);

static HMODULE g_phys;
static void *g_world, *g_gm;
static int g_step;
static uint32_t g_snap[4096];
static int g_snap_n;
static void *g_snap_obj;

/* ------------------------------------------------------------------ fake gEnv */
typedef struct NullObj { void **vt; char pad[4096]; } NullObj;
static NullObj g_null, g_cfg;
static void *g_nullvt[1024], *g_cfgvt[1024];
static void *nullfn(void *self) { (void)self; return &g_null; }
/* gEnv+112: its slot 20 returns a config block whose int at +248 limits |dir.x|, |dir.y| of
   RayWorldIntersection rays ("DrawRayOnGrid, dir.x(%.1f), dir.y(%.1f)", an ArcheAge addition)
   unless gEnv byte +289 is set. The object returns itself, so the int lives at g_cfg+248. */
static void *cfgfn(void *self) { (void)self; return &g_cfg; }
static struct { void *ptrs[64]; unsigned char bytes[4096]; } g_env;
static void install_fake_env(HMODULE h) {
  for (int i = 0; i < 1024; i++) g_nullvt[i] = (void *)nullfn;
  g_null.vt = g_nullvt;
  for (int i = 0; i < 1024; i++) g_cfgvt[i] = (void *)cfgfn;
  g_cfg.vt = g_cfgvt;
  *(int *)((char *)&g_cfg + 248) = 1000; /* default ray grid limit, see "env raymax=" */
  memset(&g_env, 0, sizeof g_env);
  g_env.ptrs[0] = &g_null;        /* +0   ISystem* */
  g_env.ptrs[112 / 8] = &g_cfg;   /* +112 config (ray grid limit at slot-20 result +248) */
  g_env.ptrs[152 / 8] = &g_null;  /* +152 log */
  g_env.ptrs[160 / 8] = &g_null;  /* +160 slot-19 void call in TimeStep */
  *(void **)VA(h, 0x3537C6D8ULL) = &g_env;
}
static LONG CALLBACK veh(PEXCEPTION_POINTERS e) {
  DWORD64 ip = e->ContextRecord->Rip;
  if (e->ExceptionRecord->ExceptionCode == 0x406D1388) return EXCEPTION_CONTINUE_SEARCH; /* thread naming */
  printf("EXCEPTION %08lx at cryphysics VA %llx addr %llx (step %d)\n", e->ExceptionRecord->ExceptionCode,
         (unsigned long long)(ip - (DWORD64)g_phys + IMAGE_BASE),
         (unsigned long long)e->ExceptionRecord->ExceptionInformation[1], g_step);
  fflush(stdout);
  ExitProcess(3);
  return EXCEPTION_CONTINUE_SEARCH;
}

/* ------------------------------------------------------------------ helpers */
static uint32_t fbits(float f) { uint32_t u; memcpy(&u, &f, 4); return u; }
static float bitsf(uint32_t u) { float f; memcpy(&f, &u, 4); return f; }
static void die(const char *msg, const char *arg) {
  printf("ERROR %s %s\n", msg, arg ? arg : "");
  fflush(stdout);
  exit(2);
}
static void out_f(const float *v, int n) {
  for (int i = 0; i < n; i++) printf(" %08x", fbits(v[i]));
}

/* value parsing: floats accept decimal, "U" (unused sentinel) or "h:XXXXXXXX" (raw bits) */
static float parse_f(const char *s) {
  if (!strcmp(s, "U")) return bitsf(UNUSED_F_BITS);
  if (!strncmp(s, "h:", 2)) return bitsf((uint32_t)strtoul(s + 2, 0, 16));
  return (float)strtod(s, 0);
}
static int parse_fv(const char *s, float *out, int maxn) {
  int n = 0;
  char buf[512];
  strncpy(buf, s, sizeof buf - 1);
  buf[sizeof buf - 1] = 0;
  for (char *t = strtok(buf, ","); t && n < maxn; t = strtok(0, ",")) out[n++] = parse_f(t);
  return n;
}
static uint32_t parse_u(const char *s) {
  if (!strcmp(s, "U")) return UNUSED_I;
  return (uint32_t)strtoll(s, 0, 0);
}

/* ------------------------------------------------------------------ named objects */
#define MAXN 128
typedef struct { char name[32]; void *p; int kind; /* 0 geom(phys_geometry*), 1 entity, 2 area */ } Named;
static Named g_obj[MAXN];
static int g_nobj;
static void *find_obj(const char *name, int kind) {
  for (int i = 0; i < g_nobj; i++)
    if (g_obj[i].kind == kind && !strcmp(g_obj[i].name, name)) return g_obj[i].p;
  return 0;
}
static void *find_target(const char *name) {
  void *p = find_obj(name, 1);
  if (!p) p = find_obj(name, 2);
  if (!p) die("unknown entity/area", name);
  return p;
}
static const char *name_of(void *p) {
  if (!p) return "-";
  for (int i = 0; i < g_nobj; i++)
    if (g_obj[i].p == p && g_obj[i].kind != 0) return g_obj[i].name;
  return "?";
}
static void add_obj(const char *name, void *p, int kind) {
  if (g_nobj >= MAXN) die("too many objects", name);
  strncpy(g_obj[g_nobj].name, name, 31);
  g_obj[g_nobj].p = p;
  g_obj[g_nobj].kind = kind;
  g_nobj++;
}

/* ------------------------------------------------------------------ tokens */
#define MAXTOK 64
typedef struct { int n; char *k[MAXTOK]; char *v[MAXTOK]; } Toks;
static void tokenize(char *line, Toks *t) {
  t->n = 0;
  for (char *s = strtok(line, " \t\r\n"); s && t->n < MAXTOK; s = strtok(0, " \t\r\n")) {
    char *eq = strchr(s, '=');
    if (eq) { *eq = 0; t->v[t->n] = eq + 1; } else t->v[t->n] = 0;
    t->k[t->n++] = s;
  }
}
static const char *get(const Toks *t, const char *key, const char *def) {
  for (int i = 1; i < t->n; i++)
    if (!strcmp(t->k[i], key)) return t->v[i] ? t->v[i] : "";
  return def;
}
static const char *need(const Toks *t, const char *key) {
  const char *v = get(t, key, 0);
  if (!v) die("missing key", key);
  return v;
}

/* ------------------------------------------------------------------ struct templates
 * x64 layouts and "unused" sentinels from entities.md / dynamics.md (see the Rust structs in
 * aaemu-physics/src/params). Only what the oracle needs; everything else is set per field. */
static void fill_unused_f(unsigned char *b, int from, int to) {
  for (int o = from; o + 4 <= to; o += 4) *(uint32_t *)(b + o) = UNUSED_F_BITS;
}
#define SETI(b, off, v) (*(uint32_t *)((b) + (off)) = (uint32_t)(v))
#define SETP(b, off, v) (*(uint64_t *)((b) + (off)) = (uint64_t)(v))
static int tmpl_params(int type, unsigned char *b) {
  memset(b, 0, 512);
  SETI(b, 0, type);
  switch (type) {
  case 0: /* pe_params_pos 72 */
    fill_unused_f(b, 4, 40);
    SETP(b, 40, 0); SETP(b, 48, 0); SETI(b, 56, UNUSED_I); SETI(b, 60, 1); b[64] = 0;
    return 72;
  case 6: /* pe_params_part 160 */
    SETI(b, 4, UNUSED_I); SETI(b, 8, UNUSED_I); SETI(b, 12, 1);
    fill_unused_f(b, 16, 48);
    SETP(b, 48, 0); SETP(b, 56, 0);
    SETI(b, 64, UNUSED_I); SETI(b, 68, 0); SETI(b, 72, 0xFFFFFFFF); SETI(b, 76, 0); SETI(b, 80, 0xFFFFFFFF);
    fill_unused_f(b, 84, 96);
    SETP(b, 96, -1); SETP(b, 104, -1); SETI(b, 112, UNUSED_I); SETP(b, 120, -1); SETI(b, 128, UNUSED_I);
    SETP(b, 136, -1); SETI(b, 144, 0); SETI(b, 152, 0); SETI(b, 156, UNUSED_I);
    return 160;
  case 10: /* simulation 84 */
    fill_unused_f(b, 4, 84);
    SETI(b, 4, UNUSED_I); SETI(b, 60, UNUSED_I); SETI(b, 64, UNUSED_I);
    return 84;
  case 12: /* buoyancy 80 */
    fill_unused_f(b, 4, 80);
    SETI(b, 72, UNUSED_I);
    return 80;
  case 14: /* bbox 28 */
    fill_unused_f(b, 4, 28);
    return 28;
  case 15: /* flags 16 */
    SETI(b, 4, UNUSED_I); SETI(b, 8, UNUSED_I); SETI(b, 12, UNUSED_I);
    return 16;
  case 23: /* timeout 12 */
    fill_unused_f(b, 4, 12);
    return 12;
  }
  return 0;
}
static int tmpl_action(int type, unsigned char *b) {
  memset(b, 0, 512);
  SETI(b, 0, type);
  switch (type) {
  case 2: /* action_impulse 80 */
    fill_unused_f(b, 4, 64);
    SETI(b, 64, UNUSED_I); SETI(b, 68, UNUSED_I); SETI(b, 72, 0); SETI(b, 76, 0);
    return 80;
  case 4: /* reset 8 */
    SETI(b, 4, 0);
    return 8;
  case 8: /* awake 12 */
    SETI(b, 4, 1); fill_unused_f(b, 8, 12);
    return 12;
  case 10: /* set_velocity 40 */
    SETI(b, 4, UNUSED_I); SETI(b, 8, UNUSED_I);
    fill_unused_f(b, 12, 36); SETI(b, 36, 0);
    return 40;
  }
  return 0;
}
/* field setters shared by params/action/status/geomparams: f<off> i<off> u<off> v<off> q<off> p<off> b<off> hex */
static void apply_fields(const Toks *t, unsigned char *b, int cap) {
  for (int i = 1; i < t->n; i++) {
    const char *k = t->k[i], *v = t->v[i];
    if (!v) continue;
    if (!strcmp(k, "hex")) {
      int n = (int)strlen(v) / 2;
      if (n > cap) die("hex too long", k);
      for (int j = 0; j < n; j++) {
        char x[3] = {v[2 * j], v[2 * j + 1], 0};
        b[j] = (unsigned char)strtoul(x, 0, 16);
      }
      continue;
    }
    char c = k[0];
    if (!strchr("fiuvqpb", c) || k[1] < '0' || k[1] > '9') continue;
    int off = atoi(k + 1);
    if (off < 0 || off + 16 > cap) die("field offset out of range", k);
    switch (c) {
    case 'f': *(float *)(b + off) = parse_f(v); break;
    case 'i': case 'u': *(uint32_t *)(b + off) = parse_u(v); break;
    case 'b': b[off] = (unsigned char)strtoul(v, 0, 0); break;
    case 'v': { float f[3] = {0}; parse_fv(v, f, 3); memcpy(b + off, f, 12); } break;
    case 'q': { float f[4] = {0}; parse_fv(v, f, 4); memcpy(b + off, f, 16); } break;
    case 'p':
      if (!strcmp(v, "null")) SETP(b, off, 0);
      else if (!strcmp(v, "unused")) SETP(b, off, -1);
      else if (!strncmp(v, "ent:", 4)) SETP(b, off, find_target(v + 4));
      else if (!strncmp(v, "geom:", 5)) { void *g = find_obj(v + 5, 0); if (!g) die("unknown geom", v); SETP(b, off, g); }
      else die("bad pointer value", v);
      break;
    }
  }
}

/* ------------------------------------------------------------------ heightfield callbacks */
static struct { int mode; float h0, kx, ky, amp; } g_hf;
static float hf_height(int ix, int iy) {
  switch (g_hf.mode) {
  case 1: return g_hf.h0 + g_hf.kx * (float)ix + g_hf.ky * (float)iy;   /* slope */
  case 2: return g_hf.h0 + g_hf.amp * (float)((ix * 7 + iy * 13) % 5); /* bumps */
  default: return g_hf.h0;                                               /* flat */
  }
}
static unsigned short hf_surf(int ix, int iy) { (void)ix; (void)iy; return 0; }

/* ------------------------------------------------------------------ trace */
#define MAXTRACE 32
static void *g_trace[MAXTRACE];
static int g_ntrace;
static void print_entity(void *e) {
  unsigned char sp[256], sd[256];
  memset(sp, 0, sizeof sp);
  SETI(sp, 0, 1); SETI(sp, 4, 0xFFFFFFFF); SETI(sp, 8, 0xFFFFFFFF);
  ((fn_stat)VT(e)[E_GetStatus])(e, sp);
  memset(sd, 0, sizeof sd);
  SETI(sd, 0, 8); SETI(sd, 4, 0xFFFFFFFF); SETI(sd, 8, 0xFFFFFFFF);
  int dyn = ((fn_stat)VT(e)[E_GetStatus])(e, sd);
  unsigned char sa[16];
  memset(sa, 0, sizeof sa);
  SETI(sa, 0, 7);
  int awake = ((fn_stat)VT(e)[E_GetStatus])(e, sa);
  printf("e %s p", name_of(e)); out_f((float *)(sp + 24), 3);
  printf(" q"); out_f((float *)(sp + 60), 4);
  if (dyn) { printf(" v"); out_f((float *)(sd + 12), 3); printf(" w"); out_f((float *)(sd + 24), 3); }
  printf(" sc %d aw %d\n", *(int *)(sp + 80), awake);
}
static void print_trace(void) {
  printf("step %d\n", g_step);
  for (int i = 0; i < g_ntrace; i++) print_entity(g_trace[i]);
}

/* ------------------------------------------------------------------ commands */
static void *box_prim_geom(const Toks *t, int type) {
  unsigned char prim[256];
  memset(prim, 0, sizeof prim);
  float c[3] = {0, 0, 0};
  const char *cs = get(t, "center", 0);
  if (cs) parse_fv(cs, c, 3);
  switch (type) {
  case 0: { /* box: Basis[9], bOriented, center, size */
    float *f = (float *)prim;
    f[0] = f[4] = f[8] = 1;
    const char *bs = get(t, "basis", 0);
    if (bs) parse_fv(bs, f, 9);
    SETI(prim, 36, bs ? 1 : 0);
    memcpy(prim + 40, c, 12);
    parse_fv(need(t, "size"), (float *)(prim + 52), 3);
  } break;
  case 4: /* sphere: center, r */
    memcpy(prim, c, 12);
    *(float *)(prim + 12) = parse_f(need(t, "r"));
    break;
  case 5: case 6: { /* cylinder/capsule: center, axis, r, hh */
    float ax[3] = {0, 0, 1};
    const char *as = get(t, "axis", 0);
    if (as) parse_fv(as, ax, 3);
    memcpy(prim, c, 12);
    memcpy(prim + 12, ax, 12);
    *(float *)(prim + 24) = parse_f(need(t, "r"));
    *(float *)(prim + 28) = parse_f(need(t, "hh"));
  } break;
  case 3: { /* ray: origin, dir */
    parse_fv(need(t, "org"), (float *)prim, 3);
    parse_fv(need(t, "dir"), (float *)(prim + 12), 3);
  } break;
  case 2: { /* heightfield primitive, 120 B */
    float *f = (float *)prim;
    f[0] = f[4] = f[8] = 1;
    int n[2] = {64, 64};
    float st[2] = {1, 1};
    sscanf(need(t, "n"), "%d,%d", &n[0], &n[1]);
    parse_fv(need(t, "step"), st, 2);
    memcpy(prim + 40, c, 12);
    memcpy(prim + 52, st, 8);
    *(float *)(prim + 60) = 1.0f / st[0];
    *(float *)(prim + 64) = 1.0f / st[1];
    SETI(prim, 68, n[0]); SETI(prim, 72, n[1]);
    SETI(prim, 76, 1); SETI(prim, 80, n[0]);
    *(float *)(prim + 84) = parse_f(get(t, "hscale", "1"));
    *(uint16_t *)(prim + 88) = 0x8000; /* typemask (must be non-zero: the ctor derives typepower from it) */
    *(uint16_t *)(prim + 90) = 0xFFFF; /* heightmask */
    SETI(prim, 92, 1);                 /* typehole: unreachable with typemask 0x8000 and surf 0 */
    SETI(prim, 96, 15);
    const char *fnm = get(t, "fn", "flat");
    g_hf.mode = !strcmp(fnm, "slope") ? 1 : !strcmp(fnm, "bumps") ? 2 : 0;
    g_hf.h0 = parse_f(get(t, "h0", "0"));
    g_hf.kx = parse_f(get(t, "kx", "0"));
    g_hf.ky = parse_f(get(t, "ky", "0"));
    g_hf.amp = parse_f(get(t, "amp", "0"));
    SETP(prim, 104, (void *)hf_height);
    SETP(prim, 112, (void *)hf_surf);
  } break;
  }
  void *g = ((fn_prim)VT(g_gm)[G_CreatePrimitive])(g_gm, type, prim);
  if (!g) die("CreatePrimitive failed", 0);
  if (type == 2 && get(t, "worldhf", 0)) {
    /* world slot 17 SetHeightfieldData(heightfield primitive*, matMapping, nMats) */
    ((fn_hfdata)VT(g_world)[W_SetHeightfieldData])(g_world, prim, 0, 0);
    /* it (re)creates one terrain entity per caller slot at world+1568+8*i; name them all "terrain" */
    for (int i = 0; i < 5; i++) {
      void *te = *(void **)((char *)g_world + 1568 + 8 * i);
      if (te && !find_obj("terrain", 1)) add_obj("terrain", te, 1);
      else if (te) { int known = 0; for (int k = 0; k < g_nobj; k++) known |= g_obj[k].p == te; if (!known) add_obj("terrain", te, 1); }
    }
  }
  return ((fn_reg)VT(g_gm)[G_RegisterGeometry])(g_gm, g, 0, 0, 0);
}
static int geom_type_id(const char *s) {
  if (!strcmp(s, "box")) return 0;
  if (!strcmp(s, "heightfield")) return 2;
  if (!strcmp(s, "ray")) return 3;
  if (!strcmp(s, "sphere")) return 4;
  if (!strcmp(s, "cylinder")) return 5;
  if (!strcmp(s, "capsule")) return 6;
  die("unknown geom type", s);
  return -1;
}
static int ent_type_id(const char *s) {
  if (!strcmp(s, "static")) return 1;
  if (!strcmp(s, "rigid")) return 2;
  if (!strcmp(s, "wheeled")) return 3;
  if (!strcmp(s, "living")) return 4;
  if (!strcmp(s, "particle")) return 5;
  return atoi(s);
}

static void cmd_rwi(const Toks *t) {
  unsigned char p[96];
  unsigned char hits[80 * 16];
  memset(p, 0, sizeof p);
  memset(hits, 0, sizeof hits);
  int nmax = atoi(get(t, "max", "1"));
  if (nmax < 1) nmax = 1;
  if (nmax > 16) nmax = 16;
  parse_fv(need(t, "org"), (float *)(p + 24), 3);
  parse_fv(need(t, "dir"), (float *)(p + 36), 3);
  SETI(p, 48, parse_u(get(t, "types", "0x13F")));
  SETI(p, 52, parse_u(get(t, "flags", "0")));
  SETP(p, 56, hits);
  SETI(p, 64, nmax);
  SETP(p, 72, 0);
  SETI(p, 80, 0);
  SETP(p, 88, 0);
  int n = ((fn_rwi)VT(g_world)[W_RayWorldIntersection])(g_world, p, "physoracle", atoi(get(t, "caller", "4")));
  printf("ray %s n %d\n", get(t, "name", "-"), n);
  for (int i = 0; i < nmax; i++) {
    unsigned char *h = hits + 80 * i;
    if (*(float *)h < 0 && i > 0) continue;
    printf("h %d d %08x e %s ip %d pid %d s %d p", i, fbits(*(float *)h), name_of(*(void **)(h + 8)), *(int *)(h + 16),
           *(int *)(h + 20), *(int *)(h + 28));
    out_f((float *)(h + 36), 3);
    printf(" n");
    out_f((float *)(h + 48), 3);
    printf("\n");
  }
}

static void run_line(char *line) {
  Toks t;
  char *hash = strchr(line, '#');
  if (hash) *hash = 0;
  tokenize(line, &t);
  if (!t.n) return;
  const char *c = t.k[0];
  if (!strcmp(c, "grid")) {
    float org[3] = {0, 0, -100}, st[2] = {4, 4};
    int n[2] = {64, 64};
    parse_fv(get(&t, "org", "0,0,-100"), org, 3);
    sscanf(get(&t, "n", "64,64"), "%d,%d", &n[0], &n[1]);
    parse_fv(get(&t, "step", "4,4"), st, 2);
    ((fn_grid)VT(g_world)[W_SetupEntityGrid])(g_world, atoi(get(&t, "axisz", "2")), org, n[0], n[1], st[0], st[1],
                                             atoi(get(&t, "pod", "0")), atoi(get(&t, "cyclic", "0")));
  } else if (!strcmp(c, "env")) { /* fake gEnv knobs: raymax=N (int), b289=0|1, b290=0|1 */
    const char *v;
    if ((v = get(&t, "raymax", 0))) *(int *)((char *)&g_cfg + 248) = atoi(v);
    if ((v = get(&t, "b289", 0))) *((unsigned char *)&g_env + 289) = (unsigned char)atoi(v);
    if ((v = get(&t, "b290", 0))) *((unsigned char *)&g_env + 290) = (unsigned char)atoi(v);
  } else if (!strcmp(c, "var")) {
    int off = atoi(need(&t, "off"));
    if (off < 104 || off > 632) die("var offset outside the physics-vars block", need(&t, "off"));
    const char *f = get(&t, "f", 0), *i = get(&t, "i", 0);
    if (f) *(float *)((char *)g_world + off) = parse_f(f);
    else if (i) *(uint32_t *)((char *)g_world + off) = parse_u(i);
    else die("var needs f= or i=", 0);
  } else if (!strcmp(c, "dumpvars")) {
    void *vars = ((fn_p)VT(g_world)[W_GetPhysVars])(g_world);
    printf("vars %d", (int)((char *)vars - (char *)g_world));
    for (int o = 0; o < 632 - 104; o += 4) printf(" %08x", *(uint32_t *)((char *)vars + o));
    printf("\n");
  } else if (!strcmp(c, "geom")) {
    add_obj(need(&t, "name"), box_prim_geom(&t, geom_type_id(need(&t, "type"))), 0);
  } else if (!strcmp(c, "entity")) {
    unsigned char pp[512];
    tmpl_params(0, pp);
    parse_fv(get(&t, "pos", "0,0,0"), (float *)(pp + 4), 3);
    float q[4] = {0, 0, 0, 1};
    parse_fv(get(&t, "q", "0,0,0,1"), q, 4);
    memcpy(pp + 16, q, 16);
    apply_fields(&t, pp, 512);
    void *e = ((fn_create)VT(g_world)[W_CreatePhysicalEntity])(g_world, ent_type_id(need(&t, "type")), 0.0f, pp, 0, 0,
                                                              atoi(get(&t, "id", "-1")), 0, 0);
    if (!e) die("CreatePhysicalEntity failed", need(&t, "name"));
    add_obj(need(&t, "name"), e, 1);
  } else if (!strcmp(c, "part")) {
    void *e = find_target(need(&t, "ent"));
    void *g = find_obj(need(&t, "geom"), 0);
    if (!g) die("unknown geom", need(&t, "geom"));
    unsigned char gp[512];
    memset(gp, 0, sizeof gp);
    float *f = (float *)gp;
    SETI(gp, 0, 0);
    f[1] = parse_f(get(&t, "density", "0"));
    f[2] = parse_f(get(&t, "mass", "0"));
    parse_fv(get(&t, "pos", "0,0,0"), f + 3, 3);
    float q[4] = {0, 0, 0, 1};
    parse_fv(get(&t, "q", "0,0,0,1"), q, 4);
    memcpy(gp + 24, q, 16);
    f[10] = parse_f(get(&t, "scale", "1"));
    SETI(gp, 64, UNUSED_I);
    SETI(gp, 68, parse_u(get(&t, "flags", "0x1809F"))); /* = DEFAULT_PART_FLAGS in the Rust crate */
    SETI(gp, 72, parse_u(get(&t, "collider", "0x1")));
    *(float *)(gp + 76) = parse_f(get(&t, "mincontact", "U"));
    SETI(gp, 80, -1);
    SETI(gp, 108, 1);
    apply_fields(&t, gp, 512);
    int id = ((fn_addg)VT(e)[E_AddGeometry])(e, g, gp, atoi(get(&t, "id", "-1")), 1);
    printf("ret part %s %d\n", need(&t, "ent"), id);
  } else if (!strcmp(c, "area")) {
    const char *kind = get(&t, "kind", "global");
    if (strcmp(kind, "global")) die("only kind=global areas are supported", kind);
    void *a = ((fn_p)VT(g_world)[W_AddGlobalArea])(g_world);
    add_obj(need(&t, "name"), a, 2);
  } else if (!strcmp(c, "params") || !strcmp(c, "action") || !strcmp(c, "status") || !strcmp(c, "getparams")) {
    void *e = find_target(need(&t, "target"));
    int type = atoi(need(&t, "type"));
    __declspec(align(16)) unsigned char b[1024];
    int sz = 0;
    if (!strcmp(get(&t, "tmpl", "auto"), "auto")) sz = (c[0] == 'a') ? tmpl_action(type, b) : (c[0] == 'p') ? tmpl_params(type, b) : 0;
    if (!sz) { memset(b, 0, sizeof b); SETI(b, 0, type); }
    apply_fields(&t, b, 512);
    int r;
    if (c[0] == 'p') r = ((fn_setp)VT(e)[E_SetParams])(e, b, 1);
    else if (c[0] == 'a') r = ((fn_setp)VT(e)[E_Action])(e, b, 1);
    else if (c[0] == 'g') r = ((fn_stat)VT(e)[E_GetParams])(e, b);
    else r = ((fn_stat)VT(e)[E_GetStatus])(e, b);
    printf("ret %s %s %d %d", c, need(&t, "target"), type, r);
    const char *dump = get(&t, "dump", 0); /* dump=OFF,NWORDS of the struct after the call */
    if (dump) {
      int off = 0, n = 0;
      sscanf(dump, "%d,%d", &off, &n);
      for (int i = 0; i < n && off + 4 * i < 512; i++) printf(" %08x", *(uint32_t *)(b + off + 4 * i));
    }
    printf("\n");
  } else if (!strcmp(c, "trace")) {
    g_ntrace = 0;
    char buf[512];
    strncpy(buf, need(&t, "ents"), sizeof buf - 1);
    buf[sizeof buf - 1] = 0;
    for (char *s = strtok(buf, ","); s && g_ntrace < MAXTRACE; s = strtok(0, ",")) g_trace[g_ntrace++] = find_target(s);
  } else if (!strcmp(c, "step")) {
    int n = atoi(get(&t, "n", "1"));
    float dt = parse_f(get(&t, "dt", "h:3d088889")); /* 1/30 */
    int flags = (int)parse_u(get(&t, "flags", "0x9F"));
    int every = atoi(get(&t, "every", "1"));
    for (int i = 0; i < n; i++) {
      ((fn_timestep)VT(g_world)[W_TimeStep])(g_world, dt, flags);
      g_step++;
      if (every > 0 && (g_step % every == 0 || i == n - 1)) print_trace();
    }
  } else if (!strcmp(c, "print")) {
    print_trace();
  } else if (!strcmp(c, "ray")) {
    cmd_rwi(&t);
  } else if (!strcmp(c, "peek")) { /* raw words of an entity/area object (RE experiments) */
    void *e = find_target(need(&t, "target"));
    int off = atoi(need(&t, "off")), n = atoi(get(&t, "n", "1"));
    printf("peek %s %d", need(&t, "target"), off);
    for (int i = 0; i < n; i++) printf(" %08x", *(uint32_t *)((char *)e + off + 4 * i));
    printf("\n");
  } else if (!strcmp(c, "snap")) { /* remember n words of an object (pointers make raw dumps non-reproducible) */
    void *e = find_target(need(&t, "target"));
    g_snap_n = atoi(get(&t, "n", "300"));
    if (g_snap_n > 4096) g_snap_n = 4096;
    g_snap_obj = e;
    memcpy(g_snap, e, 4 * (size_t)g_snap_n);
  } else if (!strcmp(c, "snapdiff")) { /* byte offsets of the words that changed since snap */
    void *e = find_target(need(&t, "target"));
    if (e != g_snap_obj) die("snapdiff target differs from snap target", need(&t, "target"));
    printf("snapdiff %s", need(&t, "target"));
    for (int i = 0; i < g_snap_n; i++)
      if (((uint32_t *)e)[i] != g_snap[i]) printf(" %d", 4 * i);
    printf("\n");
  } else if (!strcmp(c, "peekworld")) {
    int off = atoi(need(&t, "off")), n = atoi(get(&t, "n", "1"));
    printf("peekworld %d", off);
    for (int i = 0; i < n; i++) printf(" %08x", *(uint32_t *)((char *)g_world + off + 4 * i));
    printf("\n");
  } else if (!strcmp(c, "peekdll")) { /* raw words of the DLL image at VA (e.g. the params size tables) */
    uint64_t va = strtoull(need(&t, "va"), 0, 0);
    int n = atoi(get(&t, "n", "1"));
    printf("peekdll %llx", (unsigned long long)va);
    for (int i = 0; i < n; i++) printf(" %08x", *(uint32_t *)VA(g_phys, va + 4 * i));
    printf("\n");
  } else if (!strcmp(c, "listwhere")) { /* which world entity list (+664+8k, k=0..7, and +760/+816) holds the entity */
    void *e = find_target(need(&t, "target"));
    int offs[10] = {664, 672, 680, 688, 696, 704, 712, 720, 760, 816};
    printf("listwhere %s", need(&t, "target"));
    for (int k = 0; k < 10; k++) {
      int found = 0, guard = 0;
      for (char *p = *(char **)((char *)g_world + offs[k]); p && guard < 100000; p = *(char **)(p + 104), guard++)
        if (p == e) { found = 1; break; }
      if (found) printf(" %d", offs[k]);
    }
    printf("\n");
  } else if (!strcmp(c, "echo")) {
    printf("echo");
    for (int i = 1; i < t.n; i++) printf(" %s", t.k[i]);
    printf("\n");
  } else {
    die("unknown command", c);
  }
}

/* ------------------------------------------------------------------ math probes */
static uint64_t g_rng;
static uint64_t splitmix(void) {
  uint64_t z = (g_rng += 0x9E3779B97F4A7C15ULL);
  z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
  z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
  return z ^ (z >> 31);
}
/* uniform float in [lo, hi): 24 random bits, computed in single precision so Rust can reproduce it */
static float frand(float lo, float hi) { return lo + (hi - lo) * ((float)(splitmix() >> 40) * (1.0f / 16777216.0f)); }
static void rand_quat(float *q) {
  float n2;
  do {
    for (int i = 0; i < 4; i++) q[i] = frand(-1, 1);
    n2 = q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3];
  } while (n2 < 0.01f || n2 > 1.0f);
  float inv = 1.0f / sqrtf(n2);
  for (int i = 0; i < 4; i++) q[i] *= inv;
}
typedef void (*fn_q2m)(float *, const float *);
typedef void (*fn_qv)(float *, const float *, const float *);
typedef void (*fn_bstep)(void *, float);
typedef void (*fn_bupd)(void *);
typedef float (*fn_f1)(float);
typedef float (*fn_f2)(float, float);
typedef double (*fn_d1)(double);
typedef double (*fn_d2)(double, double);

static void math_probe(const char *name, int count, uint64_t seed) {
  g_rng = seed;
  HMODULE crt = GetModuleHandleA("MSVCR100.dll");
  printf("physoracle-math 1 %s %d %llu\n", name, count, (unsigned long long)seed);
  if (!strcmp(name, "quat2mat")) {
    /* sub_350247B0: Matrix33 (row-major) from quaternion (x,y,z,w) */
    fn_q2m f = (fn_q2m)VA(g_phys, 0x350247B0ULL);
    for (int i = 0; i < count; i++) {
      float q[4], m[9];
      if (i % 4 == 3) for (int k = 0; k < 4; k++) q[k] = frand(-2, 2); /* also non-unit inputs */
      else rand_quat(q);
      f(m, q);
      printf("in"); out_f(q, 4); printf(" out"); out_f(m, 9); printf("\n");
    }
  } else if (!strcmp(name, "mat2quat")) {
    /* sub_35023EB0: quaternion from a row-major Matrix33 (all four trace branches) */
    fn_q2m q2m = (fn_q2m)VA(g_phys, 0x350247B0ULL);
    fn_q2m f = (fn_q2m)VA(g_phys, 0x35023EB0ULL);
    for (int i = 0; i < count; i++) {
      float q[4], m[9], o[4];
      rand_quat(q);
      if (i % 5 == 1) { q[3] = frand(-0.05f, 0.05f); } /* push trace <= 0 */
      q2m(m, q);
      if (i % 7 == 3) for (int k = 0; k < 9; k++) m[k] += frand(-0.01f, 0.01f); /* slightly non-orthonormal */
      f(o, m);
      printf("in"); out_f(m, 9); printf(" out"); out_f(o, 4); printf("\n");
    }
  } else if (!strcmp(name, "quatrot")) {
    /* sub_35022240: rotate vec3 by quaternion */
    fn_qv f = (fn_qv)VA(g_phys, 0x35022240ULL);
    for (int i = 0; i < count; i++) {
      float q[4], v[3], o[3];
      rand_quat(q);
      for (int k = 0; k < 3; k++) v[k] = frand(-100, 100);
      f(o, q, v);
      printf("in"); out_f(q, 4); out_f(v, 3); printf(" out"); out_f(o, 3); printf("\n");
    }
  } else if (!strcmp(name, "bodystep") || !strcmp(name, "bodyupdate")) {
    /* sub_3509E180 body Step(dt) / sub_3509CE40 UpdateState on a 212-byte RigidBody:
       +0 pos, +12 q, +28 P, +40 L, +52 w, +64 v, +76 M, +80 Minv, +84 V, +88 Ibody, +100 Ibody_inv */
    int step = !strcmp(name, "bodystep");
    for (int i = 0; i < count; i++) {
      __declspec(align(16)) float b[64];
      memset(b, 0, sizeof b);
      for (int k = 0; k < 3; k++) b[k] = frand(-50, 50);
      rand_quat(b + 3);
      float m = frand(0.5f, 2000);
      for (int k = 0; k < 3; k++) b[7 + k] = frand(-50, 50) * m;     /* P */
      float wscale = (i % 3 == 0) ? 0.001f : 5.0f;                   /* hit both rotation branches */
      for (int k = 0; k < 3; k++) b[22 + k] = frand(0.05f, 10) * m; /* Ibody */
      for (int k = 0; k < 3; k++) b[25 + k] = 1.0f / b[22 + k];     /* Ibody_inv */
      for (int k = 0; k < 3; k++) b[10 + k] = frand(-wscale, wscale) * b[22 + k]; /* L ~ I*w */
      b[19] = m;
      b[20] = 1.0f / m;
      b[21] = frand(0.1f, 10);
      float dt = (i % 2) ? (1.0f / 30.0f) : frand(0.001f, 0.02f);
      printf("in"); out_f(b, 28); out_f(&dt, 1);
      if (step) ((fn_bstep)VA(g_phys, 0x3509E180ULL))(b, dt);
      else ((fn_bupd)VA(g_phys, 0x3509CE40ULL))(b);
      printf(" out"); out_f(b, 44); printf("\n");
    }
  } else if (!strcmp(name, "sinf") || !strcmp(name, "cosf") || !strcmp(name, "tanf") || !strcmp(name, "atanf") ||
             !strcmp(name, "asinf") || !strcmp(name, "acosf") || !strcmp(name, "expf") || !strcmp(name, "logf") ||
             !strcmp(name, "sqrtf")) {
    fn_f1 f = (fn_f1)GetProcAddress(crt, name);
    if (!f) die("CRT function not found", name);
    for (int i = 0; i < count; i++) {
      float x;
      if (!strcmp(name, "asinf") || !strcmp(name, "acosf")) x = frand(-1, 1);
      else if (!strcmp(name, "logf") || !strcmp(name, "sqrtf")) x = frand(0, 1000) + 1e-6f;
      else if (!strcmp(name, "expf")) x = frand(-20, 20);
      else x = (i % 2) ? frand(-4, 4) : frand(-200, 200);
      float y = f(x);
      printf("in %08x out %08x\n", fbits(x), fbits(y));
    }
  } else if (!strcmp(name, "atan2f") || !strcmp(name, "powf")) {
    fn_f2 f = (fn_f2)GetProcAddress(crt, name);
    for (int i = 0; i < count; i++) {
      float x = frand(-10, 10), y = frand(-10, 10);
      if (!strcmp(name, "powf")) x = frand(0, 10);
      float r = f(x, y);
      printf("in %08x %08x out %08x\n", fbits(x), fbits(y), fbits(r));
    }
  } else if (!strcmp(name, "sin") || !strcmp(name, "cos") || !strcmp(name, "exp") || !strcmp(name, "log") ||
             !strcmp(name, "asin")) {
    fn_d1 f = (fn_d1)GetProcAddress(crt, name);
    for (int i = 0; i < count; i++) {
      double x = !strcmp(name, "asin") ? frand(-1, 1) : !strcmp(name, "log") ? frand(0, 1000) + 1e-6 : frand(-20, 20);
      double y = f(x);
      uint64_t a, b;
      memcpy(&a, &x, 8);
      memcpy(&b, &y, 8);
      printf("in %016llx out %016llx\n", (unsigned long long)a, (unsigned long long)b);
    }
  } else if (!strcmp(name, "atan2")) {
    fn_d2 f = (fn_d2)GetProcAddress(crt, name);
    for (int i = 0; i < count; i++) {
      double x = frand(-10, 10), y = frand(-10, 10), r = f(x, y);
      uint64_t a, b, c;
      memcpy(&a, &x, 8); memcpy(&b, &y, 8); memcpy(&c, &r, 8);
      printf("in %016llx %016llx out %016llx\n", (unsigned long long)a, (unsigned long long)b, (unsigned long long)c);
    }
  } else if (!strcmp(name, "rng")) {
    for (int i = 0; i < count; i++) { float x = frand(-1, 1); printf("in %d out %08x\n", i, fbits(x)); }
  } else {
    die("unknown math probe", name);
  }
}

/* ------------------------------------------------------------------ main */
int main(int argc, char **argv) {
  _setmode(_fileno(stdout), _O_BINARY);
  setvbuf(stdout, 0, _IOFBF, 1 << 16);
  AddVectoredExceptionHandler(1, veh);
  const char *rt = getenv("AAEMU_PHYS_RT");
  if (!rt) rt = "D:\\aa-re\\cryphysics\\harness\\rt";
  int a = 1;
  if (a + 1 < argc && !strcmp(argv[a], "--rt")) { rt = argv[a + 1]; a += 2; }
  if (a >= argc) {
    fprintf(stderr, "usage: physoracle [--rt DIR] SCENARIO|-\n       physoracle [--rt DIR] --math NAME COUNT SEED\n");
    return 1;
  }
  char dll[MAX_PATH];
  snprintf(dll, sizeof dll, "%s\\cryphysics_dedicate.dll", rt);
  SetDllDirectoryA(rt);
  g_phys = LoadLibraryA(dll);
  if (!g_phys) { printf("ERROR LoadLibrary %s failed (%lu)\n", dll, GetLastError()); return 1; }
  if (!strcmp(argv[a], "--math")) {
    if (a + 3 >= argc) return 1;
    math_probe(argv[a + 1], atoi(argv[a + 2]), strtoull(argv[a + 3], 0, 0));
    fflush(stdout);
    return 0;
  }
  g_world = ((void *(*)(void *))GetProcAddress(g_phys, "CreatePhysicalWorld"))(NULL);
  if (!g_world) { printf("ERROR CreatePhysicalWorld failed\n"); return 1; }
  install_fake_env(g_phys);
  g_gm = ((fn_p)VT(g_world)[W_GetGeomManager])(g_world);

  FILE *f = strcmp(argv[a], "-") ? fopen(argv[a], "rb") : stdin;
  if (!f) die("cannot open scenario", argv[a]);
  printf("physoracle 1\n");
  char line[4096];
  while (fgets(line, sizeof line, f)) run_line(line);
  fflush(stdout);
  return 0;
}
