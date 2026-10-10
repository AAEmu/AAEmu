/* p1c_common.h: shared helpers of the P1c probes (trimesh / heightfield). Each probe is one C
 * file that #includes this header; it loads the real x64 cryphysics_dedicate.dll (10.0.2.13)
 * standalone and calls its CTriMesh / CHeightfield code directly. Nothing of the reference
 * binaries is modified or committed.
 *
 * Build:  tests\p1c_build_probe.bat <probe.c> <out.exe>   (MSVC 2022 x64)
 *
 * Provided here:
 *   p1c_init(&argc, &argv)        load the DLL (--rt DIR / %AAEMU_PHYS_RT%), install a fake gEnv
 *   VA(va), VSLOT(obj, n)         addresses inside the DLL, vtable slots
 *   p1c_stream_*                  a CMemStream (+8 buf, +20 pos, +24 cap, +28 own, +32 measure)
 *   p1c_mesh_new(...)             CTriMesh ctor 0x351BF470 + constructor-from-data 0x351CF1B0
 *   p1c_mesh_dump(tm)             every field of a CTriMesh as raw words ("mesh ..." block, the
 *                                 format tests/p1c_common/mod.rs parses back into a TriMesh)
 *   p1c_read_file(path, &len)     whole file
 *   fb(f), prw(tag, p, n)         raw bits printing
 */
#ifndef P1C_COMMON_H
#define P1C_COMMON_H
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <math.h>

#define IMAGE_BASE 0x35000000ULL
static HMODULE g_phys;
#define VA(va) ((void *)((char *)g_phys + ((unsigned long long)(va) - IMAGE_BASE)))
#define VSLOT(obj, n) ((*(void ***)(obj))[n])

static uint32_t fb(float f) { uint32_t u; memcpy(&u, &f, 4); return u; }
static float bf(uint32_t u) { float f; memcpy(&f, &u, 4); return f; }
static uint64_t db(double d) { uint64_t u; memcpy(&u, &d, 8); return u; }

static void prw(const char *tag, const void *p, int words) {
  printf("%s", tag);
  for (int i = 0; i < words; i++) printf(" %08x", ((const uint32_t *)p)[i]);
  printf("\n");
}

static unsigned char *p1c_read_file(const char *path, long *len) {
  FILE *f = fopen(path, "rb");
  if (!f) { printf("ERROR open %s\n", path); exit(1); }
  fseek(f, 0, SEEK_END);
  long n = ftell(f);
  fseek(f, 0, SEEK_SET);
  unsigned char *b = (unsigned char *)malloc(n + 16);
  if (fread(b, 1, n, f) != (size_t)n) { printf("ERROR read %s\n", path); exit(1); }
  fclose(f);
  *len = n;
  return b;
}

/* ---- fake gEnv (same as physoracle.c): interface pointers to a null object whose virtuals
 * return the object itself; +112 config object with the RWI ray limit at +248. */
typedef struct P1cNullObj { void **vt; char pad[4096]; } P1cNullObj;
static P1cNullObj g_p1c_null, g_p1c_cfg;
static void *g_p1c_nullvt[1024], *g_p1c_cfgvt[1024];
static void *p1c_nullfn(void *self) { (void)self; return &g_p1c_null; }
static void *p1c_cfgfn(void *self) { (void)self; return &g_p1c_cfg; }
static struct { void *ptrs[64]; unsigned char bytes[4096]; } g_p1c_env;
static void p1c_install_env(void) {
  for (int i = 0; i < 1024; i++) g_p1c_nullvt[i] = (void *)p1c_nullfn;
  g_p1c_null.vt = g_p1c_nullvt;
  for (int i = 0; i < 1024; i++) g_p1c_cfgvt[i] = (void *)p1c_cfgfn;
  g_p1c_cfg.vt = g_p1c_cfgvt;
  *(int *)((char *)&g_p1c_cfg + 248) = 1000;
  memset(&g_p1c_env, 0, sizeof g_p1c_env);
  g_p1c_env.ptrs[0] = &g_p1c_null;
  g_p1c_env.ptrs[112 / 8] = &g_p1c_cfg;
  g_p1c_env.ptrs[152 / 8] = &g_p1c_null;
  g_p1c_env.ptrs[160 / 8] = &g_p1c_null;
  *(void **)VA(0x3537C6D8ULL) = &g_p1c_env;
}

static LONG CALLBACK p1c_veh(PEXCEPTION_POINTERS e) {
  DWORD64 ip = e->ContextRecord->Rip;
  if (e->ExceptionRecord->ExceptionCode == 0x406D1388) return EXCEPTION_CONTINUE_SEARCH;
  printf("EXCEPTION %08lx at cryphysics VA %llx addr %llx\n", e->ExceptionRecord->ExceptionCode,
         (unsigned long long)(ip - (DWORD64)g_phys + IMAGE_BASE),
         (unsigned long long)e->ExceptionRecord->ExceptionInformation[1]);
  fflush(stdout);
  ExitProcess(3);
  return EXCEPTION_CONTINUE_SEARCH;
}

/* Loads the DLL; consumes a leading "--rt DIR" from argv. */
static void p1c_init(int *argc, char ***argv) {
  const char *rt = getenv("AAEMU_PHYS_RT");
  if (!rt) rt = "D:\\aa-re\\cryphysics\\harness\\rt";
  if (*argc > 2 && !strcmp((*argv)[1], "--rt")) {
    rt = (*argv)[2];
    (*argv)[2] = (*argv)[0];
    *argv += 2;
    *argc -= 2;
  }
  char dll[MAX_PATH];
  snprintf(dll, sizeof dll, "%s\\cryphysics_dedicate.dll", rt);
  SetDllDirectoryA(rt);
  g_phys = LoadLibraryA(dll);
  if (!g_phys) { printf("ERROR LoadLibrary %s failed (%lu)\n", dll, GetLastError()); exit(1); }
  p1c_install_env();
  AddVectoredExceptionHandler(1, p1c_veh);
}

/* ---- CMemStream ---- */
typedef struct P1cStream {
  void **vt;         /* +0 */
  unsigned char *buf; /* +8 */
  int pad16;         /* +16 */
  int pos;           /* +20 */
  int cap;           /* +24 */
  unsigned char own; /* +28 */
  unsigned char pad29[3];
  int measure;       /* +32 */
  int pad36;
} P1cStream;
static void p1c_stream_dtor(void *self, char del) { (void)self; (void)del; }
static void p1c_stream_grow(P1cStream *s, int n) {
  int ncap = ((s->pos + n - 1) & ~0xFFF) + 4096;
  unsigned char *nb = (unsigned char *)malloc(ncap);
  if (s->buf) memcpy(nb, s->buf, s->cap);
  s->buf = nb;
  s->cap = ncap;
}
static void *g_p1c_stream_vt[2] = {(void *)p1c_stream_dtor, (void *)p1c_stream_grow};
static P1cStream *p1c_stream_read(const unsigned char *data, int len) {
  P1cStream *s = (P1cStream *)calloc(1, sizeof *s);
  s->vt = g_p1c_stream_vt;
  s->buf = (unsigned char *)malloc(len + 64);
  memcpy(s->buf, data, len);
  memset(s->buf + len, 0, 64);
  s->cap = len;
  return s;
}
static P1cStream *p1c_stream_write(int measure) {
  P1cStream *s = (P1cStream *)calloc(1, sizeof *s);
  s->vt = g_p1c_stream_vt;
  s->cap = 1 << 16;
  s->buf = (unsigned char *)calloc(1, s->cap);
  s->measure = measure;
  return s;
}
static void p1c_print_bytes(const char *tag, const unsigned char *b, int n) {
  printf("%s %d ", tag, n);
  for (int i = 0; i < n; i++) printf("%02x", b[i]);
  printf("\n");
}

/* ---- CTriMesh ---- */
typedef struct { void *p; int stride; } P1cStrided;
typedef void *(*p1c_fn_tm_ctor)(void *self);
typedef void (*p1c_fn_tm_init)(void *self, P1cStrided *v, P1cStrided *idx, void *mats, void *foreign, int nTris,
                               int flags, int minTris, int maxTris, int obbFactorBits);

/* A CTriMesh built by the DLL from float vertices and int indices (stride 4; with flag 2 the
 * index array is used in place, otherwise it is copied as u16 — pass 16-bit indices then).
 * mats / foreign may be NULL. */
static unsigned char *p1c_mesh_new(float *v, void *idx, int idx_stride, signed char *mats, int *foreign, int nTris,
                                   int flags, int minTris, int maxTris, float obbFactor) {
  unsigned char *tm = (unsigned char *)calloc(1, 0x260);
  ((p1c_fn_tm_ctor)VA(0x351BF470ULL))(tm);
  P1cStrided sv = {v, 12}, si = {idx, idx_stride};
  ((p1c_fn_tm_init)VA(0x351CF1B0ULL))(tm, &sv, &si, mats, foreign, nTris, flags, minTris, maxTris,
                                      (int)fb(obbFactor));
  return tm;
}

/* A CTriMesh from a CGF physics blob's trimesh part (stream positioned after the geometry
 * type id): ctor + slot 26 Load(stream, pVtx, pIdx, pMats). */
typedef void (*p1c_fn_tm_load)(void *self, P1cStream *s, P1cStrided *vtx, P1cStrided *idx, void *mats);
static unsigned char *p1c_mesh_load(P1cStream *s, P1cStrided *vtx, P1cStrided *idx, void *mats) {
  unsigned char *tm = (unsigned char *)calloc(1, 0x260);
  ((p1c_fn_tm_ctor)VA(0x351BF470ULL))(tm);
  ((p1c_fn_tm_load)VSLOT(tm, 26))(tm, s, vtx, idx, mats);
  return tm;
}

/* Every field of a CTriMesh (format parsed by tests/p1c_common/mod.rs). */
static void p1c_mesh_dump(unsigned char *tm) {
  int nt = *(int *)(tm + 120), nv = *(int *)(tm + 124);
  printf("mesh nt %d nv %d flags %08x valency %d mvd %08x prio %d convex %d multi %d raygrid %d vmin %d\n", nt, nv,
         *(uint32_t *)(tm + 152), *(int *)(tm + 168), *(uint32_t *)(tm + 16), *(int *)(tm + 20), *(int *)(tm + 24),
         *(int *)(tm + 512), *(int *)(tm + 476), *(int *)(tm + 600));
  int *idx = *(int **)(tm + 56);
  printf("idx");
  for (int i = 0; idx && i < 3 * nt; i++) printf(" %d", idx[i]);
  printf("\n");
  unsigned char *vtx = *(unsigned char **)(tm + 80);
  int stride = *(int *)(tm + 88);
  printf("vtx");
  for (int i = 0; vtx && i < nv; i++)
    for (int k = 0; k < 3; k++) printf(" %08x", *(uint32_t *)(vtx + (size_t)i * stride + 4 * k));
  printf("\n");
  float *nrm = *(float **)(tm + 96);
  printf("nrm");
  for (int i = 0; nrm && i < 3 * nt; i++) printf(" %08x", fb(nrm[i]));
  printf("\n");
  int *topo = *(int **)(tm + 112);
  printf("topo");
  for (int i = 0; topo && i < 3 * nt; i++) printf(" %d", topo[i]);
  printf("\n");
  signed char *mats = *(signed char **)(tm + 64);
  if (mats) { printf("mats"); for (int i = 0; i < nt; i++) printf(" %d", mats[i]); printf("\n"); }
  else printf("mats -\n");
  int *fid = *(int **)(tm + 72);
  if (fid) { printf("fid"); for (int i = 0; i < nt; i++) printf(" %d", fid[i]); printf("\n"); }
  else printf("fid -\n");
  int *vmap = *(int **)(tm + 104);
  if (vmap) { printf("vmap"); for (int i = 0; i < nv; i++) printf(" %d", vmap[i]); printf("\n"); }
  else printf("vmap -\n");
  int *aux = *(int **)(tm + 160);
  if (aux) { printf("aux"); for (int i = 0; i < nt; i++) printf(" %d", aux[i]); printf("\n"); }
  else printf("aux -\n");
  uint32_t *isl = *(uint32_t **)(tm + 128);
  if (isl) {
    int n = *(int *)(tm + 136);
    printf("islands %d", n);
    for (int i = 0; i < 10 * n; i++) printf(" %08x", isl[i]);
    printf("\n");
  } else printf("islands -\n");
  uint32_t *til = *(uint32_t **)(tm + 144);
  if (til) { printf("tisl"); for (int i = 0; i < nt; i++) printf(" %08x", til[i]); printf("\n"); }
  else printf("tisl -\n");
  prw("cache", tm + 480, 4 + 4 + 1 + 6); /* +480 convex[4], +496 tol[4], +512, +516..+536 */
  printf("misc %d %d\n", *(int *)(tm + 540), *(int *)(tm + 588));
  void *t = *(void **)(tm + 48);
  if (t) {
    int ty = ((int (*)(void *))VSLOT(t, 1))(t);
    if (ty == 0 && *(int *)((unsigned char *)t + 24) > 1) {
      /* COBBTree node 1 is never used: the builder writes only its iParent (+60) and nTris
       * (+68); the rest is uninitialised heap. Zero it so dumps are reproducible (the port's
       * node 1 is zero there). */
      unsigned char *n1 = *(unsigned char **)((unsigned char *)t + 16) + 72;
      int ip = *(int *)(n1 + 60), nt1 = *(int *)(n1 + 68);
      memset(n1, 0, 72);
      *(int *)(n1 + 60) = ip;
      *(int *)(n1 + 68) = nt1;
    }
    P1cStream *s = p1c_stream_write(0);
    ((void (*)(void *, P1cStream *))VSLOT(t, 21))(t, s);
    printf("tree %d ", ty);
    p1c_print_bytes("bytes", s->buf, s->pos);
  } else printf("tree -\n");
}
#endif
