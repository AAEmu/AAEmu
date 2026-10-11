/* CryPhysics oracle harness: drives the real ArcheAge 10.x zone-server physics
   (cryphysics_dedicate.dll, x64) without ISystem. Creates a world, a static ground box and a
   rigid 1 m box above it, steps 1/30 s and prints the rigid body pose (float + raw bits).
   usage: oracle_box.exe <runtime-dir> [steps] [water]
   env: BMASS (box mass, def 10), GPZ (ground z, def -0.5), WATERZ (water plane z, def 2),
        GFLAGS (part flags), ORGZ (entity-grid origin z, def -100), PRINTALL.
   Layouts/slots come from the RE of the binary (rust/docs/physics/CRYPHYSICS-MAP.md). */
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include <stdlib.h>

typedef void *(*PFN_CreatePhysicalWorld)(void *pSystem);
#define VT(obj) (*(void ***)(obj))

/* ---- IPhysicalWorld slots (x64 10.x) ---- */
#define W_SetupEntityGrid 11      /* (int axisz, Vec3* org, int nx, int ny, float stepx, float stepy, int log2PODscale, int bCyclic) */
#define W_GetGeomManager 9        /* () -> IGeomManager* (world+16) */
#define W_GetPhysVars 20          /* () -> PhysicsVars* (world+104) */
#define W_CreatePhysicalEntity 21 /* (int type, float lifeTime, pe_params*, void* pForeignData, int iForeignData, int id, IPhysicalEntity* pHost, unsigned flags) */
#define W_TimeStep 32             /* (float dt, int flags) */
/* ---- IGeomManager slots ---- */
#define G_CreatePrimitive 7       /* (int type, primitive*) -> IGeometry* ; type 0 = box */
#define G_RegisterGeometry 9      /* (IGeometry*, int defSurfaceIdx, int* pMatMapping, int nMats) -> phys_geometry* */
/* ---- IPhysicalEntity slots ---- */
#define E_SetParams 7             /* (pe_params*, int bThreadSafe) */
#define E_GetStatus 9             /* (pe_status*) */
#define E_AddGeometry 11          /* (phys_geometry*, pe_geomparams*, int id, int bThreadSafe) */

/* "unused" markers as the binary tests them: float bits & 0xFFA00000 == 0xFFA00000, int == 0x80000000 */
static float unusedf(void) { uint32_t u = 0xFFBFFFFF; float f; memcpy(&f, &u, 4); return f; }
#define UNUSED_I 0x80000000

#pragma pack(push, 1)
typedef struct { float basis[9]; int bOriented; float center[3]; float size[3]; } prim_box; /* 64 bytes */
typedef struct {
  int type;           /* 0 = pe_params_pos */
  float pos[3];       /* +4 */
  float q[4];         /* +16 (x,y,z,w) */
  float scale;        /* +32 */
  int pad0;
  void *pMtx3x4;      /* +40 */
  void *pMtx3x3;      /* +48 */
  int iSimClass;      /* +56 */
  int bRecalcBounds;  /* +60 */
  char tail[64];
} pe_params_pos;
typedef struct {
  int type;           /* 0 = pe_geomparams */
  float density;      /* +4 */
  float mass;         /* +8 */
  float pos[3];       /* +12 */
  float q[4];         /* +24 */
  float scale;        /* +40 */
  int pad0;
  void *pMtx3x4;      /* +48 */
  void *pMtx3x3;      /* +56 */
  int surface_idx;    /* +64 */
  unsigned flags;     /* +68 */
  unsigned flagsCollider; /* +72 */
  float minContactDist;   /* +76 */
  int idmatBreakable;     /* +80 */
  int pad1;
  void *pLattice;     /* +88 */
  int *pMatMapping;   /* +96 */
  int nMats;          /* +104 */
  int bRecalcBBox;    /* +108 */
  char tail[64];
} pe_geomparams;
typedef struct {
  int type;           /* 1 = pe_status_pos */
  int partid;         /* +4 */
  int ipart;          /* +8 */
  unsigned flags;     /* +12 */
  unsigned flagsOR, flagsAND; /* +16,+20 */
  float pos[3];       /* +24 */
  float BBox[2][3];   /* +36 */
  float q[4];         /* +60 */
  float scale;        /* +76 */
  int iSimClass;      /* +80 */
  int pad0;
  void *pMtx3x4;      /* +88 */
  void *pMtx3x3;      /* +96 */
  void *pGeom, *pGeomProxy; /* +104,+112 */
  char tail[64];
} pe_status_pos;
#pragma pack(pop)

typedef void *(*fn_p)(void *);
typedef void (*fn_grid)(void *, int, float *, int, int, float, float, int, int);
typedef void *(*fn_create)(void *, int, float, void *, void *, int, int, void *, unsigned);
typedef int (*fn_timestep)(void *, float, int);
typedef void *(*fn_prim)(void *, int, void *);
typedef void *(*fn_reg)(void *, void *, int, int *, int);
typedef int (*fn_setp)(void *, void *, int);
typedef int (*fn_stat)(void *, void *);
typedef int (*fn_addg)(void *, void *, void *, int, int);

static void init_pos(pe_params_pos *p, float x, float y, float z) {
  memset(p, 0, sizeof *p);
  p->type = 0; p->pos[0] = x; p->pos[1] = y; p->pos[2] = z;
  p->q[0] = p->q[1] = p->q[2] = 0; p->q[3] = 1; p->scale = unusedf();
  p->pMtx3x4 = 0; p->pMtx3x3 = 0; p->iSimClass = UNUSED_I; p->bRecalcBounds = 1;
}
static void init_geom(pe_geomparams *g, float mass) {
  memset(g, 0, sizeof *g);
  g->type = 0; g->density = 0; g->mass = mass; g->q[3] = 1; g->scale = 1;
  g->surface_idx = UNUSED_I; g->flags = 0x1009F; /* collision-class bits (exact meaning per bit inferred) + 0x10000 = part takes part in buoyancy */
  { const char *e = getenv("GFLAGS"); if (e) g->flags = strtoul(e, 0, 0); }
  g->flagsCollider = 0x1; g->minContactDist = unusedf(); g->idmatBreakable = -1; g->bRecalcBBox = 1;
}
static void *box_geom(void *gm, float hx, float hy, float hz) {
  prim_box b; memset(&b, 0, sizeof b);
  b.basis[0] = b.basis[4] = b.basis[8] = 1; b.bOriented = 0;
  b.size[0] = hx; b.size[1] = hy; b.size[2] = hz;
  void *g = ((fn_prim)VT(gm)[G_CreatePrimitive])(gm, 0, &b);
  return ((fn_reg)VT(gm)[G_RegisterGeometry])(gm, g, 0, 0, 0);
}
static void hexv(const char *tag, const float *v, int n) {
  printf("%s", tag);
  for (int i = 0; i < n; i++) { uint32_t u; memcpy(&u, &v[i], 4); printf(" %.6f(%08x)", v[i], u); }
}

static HMODULE g_phys;
/* Minimal stand-in for the engine's global environment (gEnv). The DLL keeps the pointer in a
   global (VA 0x3537C6D8 in 10.x dedicate); CreatePhysicalWorld(NULL) leaves it NULL, and
   TimeStep dereferences gEnv+0xA0 (an interface whose slot 19 is a void call). We point it at
   a zeroed block whose interface pointers are "null objects": every virtual returns the null
   object itself, so pointer-returning getters stay valid and void calls are no-ops. */
typedef struct NullObj { void **vt; char pad[4096]; } NullObj;
static NullObj g_null;
static void *g_nullvt[1024];
static void *nullfn(void *self) { (void)self; return &g_null; }
static struct { void *ptrs[64]; unsigned char bytes[4096]; } g_env;
static void install_fake_env(HMODULE h) {
  for (int i = 0; i < 1024; i++) g_nullvt[i] = (void *)nullfn;
  g_null.vt = g_nullvt;
  memset(&g_env, 0, sizeof g_env);
  g_env.ptrs[0] = &g_null;            /* +0   ISystem*            */
  g_env.ptrs[112 / 8] = &g_null;      /* +112 (slot 20 getter -> struct with dword at +248) */
  g_env.ptrs[152 / 8] = &g_null;      /* +152 ILog-like (module factory only) */
  g_env.ptrs[160 / 8] = &g_null;      /* +160 (slot 19 void call in TimeStep / pool growth) */
  *(void **)((char *)h + 0x37C6D8) = &g_env;
}
static LONG CALLBACK veh(PEXCEPTION_POINTERS e) {
  DWORD64 ip = e->ContextRecord->Rip;
  printf("EXCEPTION %08lx at %p (cryphysics VA %llx) addr %llx\n", e->ExceptionRecord->ExceptionCode, (void *)ip,
         (unsigned long long)(ip - (DWORD64)g_phys + 0x35000000ULL),
         (unsigned long long)e->ExceptionRecord->ExceptionInformation[1]);
  ExitProcess(3);
  return EXCEPTION_CONTINUE_SEARCH;
}
int main(int argc, char **argv) {
  setvbuf(stdout, 0, _IONBF, 0);
  AddVectoredExceptionHandler(1, veh);
  const char *rt = argc > 1 ? argv[1] : "D:/aa-re/cryphysics/harness/rt";
  char dll[512]; sprintf(dll, "%s/cryphysics_dedicate.dll", rt);
  SetDllDirectoryA(rt);
  HMODULE h = LoadLibraryA(dll);
  g_phys = h;
  if (!h) { printf("LoadLibrary failed %lu\n", GetLastError()); return 1; }
  void *w = ((PFN_CreatePhysicalWorld)GetProcAddress(h, "CreatePhysicalWorld"))(NULL);
  printf("world %p\n", w);
  install_fake_env(h);

  float org[3] = {0, 0, (float)atof(getenv("ORGZ")?getenv("ORGZ"):"-100")};
  ((fn_grid)VT(w)[W_SetupEntityGrid])(w, 2, org, 64, 64, 4.0f, 4.0f, 0, 0);
  float *vars = (float *)((fn_p)VT(w)[W_GetPhysVars])(w);
  hexv("physvars gravity?", (float *)((char *)vars + (284 - 104)), 3); printf("\n");

  void *gm = ((fn_p)VT(w)[W_GetGeomManager])(w);
  void *pg_ground = box_geom(gm, (float)atof(getenv("GX")?getenv("GX"):"50"), (float)atof(getenv("GX")?getenv("GX"):"50"), 0.5f);
  void *pg_box = box_geom(gm, 0.5f, 0.5f, 0.5f);
  printf("geoms %p %p\n", pg_ground, pg_box);

  pe_params_pos pp;
  init_pos(&pp, (float)atof(getenv("GPX")?getenv("GPX"):"32"), 32, (float)atof(getenv("GPZ")?getenv("GPZ"):"-0.5"));
  void *ground = ((fn_create)VT(w)[W_CreatePhysicalEntity])(w, 1, 0.0f, &pp, 0, 0, -1, 0, 0);
  pe_geomparams gp; init_geom(&gp, 0);
  int r1 = ((fn_addg)VT(ground)[E_AddGeometry])(ground, pg_ground, &gp, -1, 1);

  init_pos(&pp, 32, 32, 5.0f);
  void *body = ((fn_create)VT(w)[W_CreatePhysicalEntity])(w, 2, 0.0f, &pp, 0, 0, -1, 0, 0);
  init_geom(&gp, (float)atof(getenv("BMASS")?getenv("BMASS"):"10"));
  int r2 = ((fn_addg)VT(body)[E_AddGeometry])(body, pg_box, &gp, -1, 1);
  printf("ground %p (part %d) body %p (part %d)\n", ground, r1, body, r2);
  {
    void *ents[2] = {ground, body};
    for (int k = 0; k < 2; k++) {
      pe_status_pos sp; memset(&sp, 0, sizeof sp); sp.type = 1; sp.partid = -1; sp.ipart = -1;
      ((fn_stat)VT(ents[k])[E_GetStatus])(ents[k], &sp);
      printf("%s", k ? "body  " : "ground"); hexv(" pos", sp.pos, 3); hexv(" bbox0", sp.BBox[0], 3); hexv(" bbox1", sp.BBox[1], 3);
      printf(" simclass %d\n", sp.iSimClass);
    }
  }

  if (argc > 3 && !strcmp(argv[3], "water")) {
    /* global area + water plane (pe_params_buoyancy, type 12, layout from the area SetParams) */
    void *area = ((fn_p)VT(w)[80])(w);
    float pb[20]; float U = unusedf();
    for (int i = 0; i < 20; i++) pb[i] = U;
    ((int *)pb)[0] = 12;
    pb[1] = 1000.0f;              /* +4  water density */
    pb[4] = 1000.0f;              /* +16 water resistance */
    pb[6] = pb[7] = pb[8] = 0;    /* +24 flow */
    pb[10] = 0; pb[11] = 0; pb[12] = 1;            /* +40 plane normal */
    pb[13] = 32; pb[14] = 32; pb[15] = (float)atof(getenv("WATERZ") ? getenv("WATERZ") : "2"); /* +52 plane origin */
    ((int *)pb)[18] = 0;          /* +72 iMedium */
    int ok = ((fn_setp)VT(area)[E_SetParams])(area, pb, 1);
    /* entity side: the same struct on the body sets kwaterDensity = density/1000 (default 0.001 -> almost no lift) */
    float eb[20];
    for (int i = 0; i < 20; i++) eb[i] = U;
    ((int *)eb)[0] = 12; eb[1] = 1000.0f; eb[4] = 1000.0f; ((int *)eb)[18] = 0x80000000;
    int ok2 = ((fn_setp)VT(body)[E_SetParams])(body, eb, 1);
    printf("body buoyancy set -> %d\n", ok2);
    printf("global area %p, buoyancy set -> %d\n", area, ok);
  }
  int steps = argc > 2 ? atoi(argv[2]) : 90;
  for (int i = 0; i <= steps; i++) {
    pe_status_pos sp; memset(&sp, 0, sizeof sp); sp.type = 1; sp.partid = -1; sp.ipart = -1;
    ((fn_stat)VT(body)[E_GetStatus])(body, &sp);
    if (getenv("PRINTALL") || i % 5 == 0 || i == steps) { printf("step %3d", i); hexv(" pos", sp.pos, 3); hexv(" q", sp.q, 4); printf("\n"); }
    ((fn_timestep)VT(w)[W_TimeStep])(w, 1.0f / 30.0f, 0x9F);
  }
  return 0;
}
