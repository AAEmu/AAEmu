/* p1b_probe: drives the BV-tree builders and helpers *inside* the real x64
 * cryphysics_dedicate.dll (10.0.2.13) on a mesh file and prints the results as raw bits.
 * It is the oracle for tests/p1b_oracle.rs (P1b, AAEMU_PHYS_P1B_PROBE=<exe>); the reference
 * binaries are only loaded, never modified or committed.
 *
 * Build (MSVC x64):  cl /nologo /O2 p1b_probe.c /Fe:p1b_probe.exe
 * Run:               p1b_probe [--rt DIR] CMD MESHFILE [args]
 *   mesh file: u32 nVerts, u32 nTris, f32 verts[3*nVerts], i32 idx[3*nTris] (little endian)
 *   aabb MESH minTris maxTris skipRel flat [b00..b22]  CAABBTree::Build 0x35139490 (+setup 0x35136960)
 *   obb  MESH minTris maxTris skipRel                    COBBTree::Build 0x35195210
 *   fit  MESH                                            sub_35195400 (all triangles)
 *   hull MESH                                            sub_35128440 (the vertices)
 *   mesh MESH flags minTris maxTris obbFactor           CTriMesh constructor 0x351CF1B0 (tree choice)
 *   slots MESH aabb|obb minTris maxTris skipRel        BV slots 5-17, 19, 25 on the built tree
 *   prims                                               CSingleBoxTree / CRayBV slots on a fixed box / ray
 *   sweep COUNT SEED                                    the swept box sub_35135490 on generated boxes
 * The runtime directory (--rt, %AAEMU_PHYS_RT%, default D:\aa-re\cryphysics\harness\rt) is the
 * one physoracle uses. */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <math.h>

#define IMAGE_BASE 0x35000000ULL
#define VA(h, va) ((void *)((char *)(h) + ((va) - IMAGE_BASE)))

static HMODULE g_phys;
static uint32_t fb(float f) { uint32_t u; memcpy(&u, &f, 4); return u; }
static uint64_t db(double d) { uint64_t u; memcpy(&u, &d, 8); return u; }

typedef struct { int nv, nt; float *v; int *idx; } Mesh;

static Mesh load_mesh(const char *path) {
  Mesh m = {0};
  FILE *f = fopen(path, "rb");
  if (!f) { printf("ERROR open %s\n", path); exit(1); }
  fread(&m.nv, 4, 1, f); fread(&m.nt, 4, 1, f);
  m.v = (float *)calloc(3 * (size_t)m.nv + 3, 4);
  m.idx = (int *)calloc(3 * (size_t)m.nt + 3, 4);
  fread(m.v, 4, 3 * (size_t)m.nv, f);
  fread(m.idx, 4, 3 * (size_t)m.nt, f);
  fclose(f);
  return m;
}

/* The CTriMesh fields the builders read (geometry.md 1.3). */
static unsigned char *fake_mesh(Mesh *m) {
  unsigned char *g = (unsigned char *)calloc(1, 0x260);
  *(int **)(g + 56) = m->idx;
  *(void **)(g + 64) = NULL;
  *(void **)(g + 72) = NULL;
  *(float **)(g + 80) = m->v;
  *(int *)(g + 88) = 12;
  *(int *)(g + 120) = m->nt;
  *(int *)(g + 124) = m->nv;
  *(void **)(g + 160) = NULL;
  return g;
}

static void print_idx(Mesh *m) {
  printf("idx");
  for (int i = 0; i < 3 * m->nt; i++) printf(" %d", m->idx[i]);
  printf("\n");
}

typedef float (*fn_build)(void *tree, void *geom);
typedef short (*fn_aabb_setup)(void *tree, int minTris, int maxTris, float skip, float *basis, int flat);

static void dump_aabb(unsigned char *t, float cost, int nt) {
  int nn = *(int *)(t + 24);
  unsigned char *nodes = *(unsigned char **)(t + 16);
  printf("aabb cost %08x nnodes %d alloc %d\n", fb(cost), nn, *(int *)(t + 28));
  printf("root");
  for (int i = 0; i < 15; i++) printf(" %08x", *(uint32_t *)(t + 32 + 4 * i));
  printf("\nfields %d %d %d %d %d %08x\n", *(short *)(t + 92), *(short *)(t + 94), *(int *)(t + 104),
         *(int *)(t + 108), *(int *)(t + 112), *(uint32_t *)(t + 120));
  printf("maxleaf %d\n", *(int *)(t + 116));
  for (int i = 0; i < nn; i++) {
    unsigned char *n = nodes + 12 * i;
    /* byte 11 is uninitialised for internal nodes and node 1; print it anyway */
    printf("node %d %d %d %d %d %d %d %d %d %d\n", i, *(int *)n, n[4], n[5], n[6], n[7], n[8], n[9], n[10], n[11]);
  }
  int nmap = ((nt - 1) >> (5 - *(int *)(t + 104))) + 1;
  uint32_t *map = *(uint32_t **)(t + 96);
  printf("map");
  for (int i = 0; i < nmap; i++) printf(" %08x", map[i]);
  printf("\n");
}

static void dump_obb(unsigned char *t, float cost, int nt) {
  int nn = *(int *)(t + 24);
  unsigned char *nodes = *(unsigned char **)(t + 16);
  printf("obb cost %08x nnodes %d alloc %d maxleaf %d skip %08x\n", fb(cost), nn, *(int *)(t + 28),
         *(int *)(t + 40), *(uint32_t *)(t + 52));
  for (int i = 0; i < nn; i++) {
    uint32_t *w = (uint32_t *)(nodes + 72 * i);
    printf("node %d", i);
    for (int k = 0; k < 18; k++) printf(" %08x", w[k]);
    printf("\n");
  }
  int *leaf = *(int **)(t + 32);
  printf("leaf");
  for (int i = 0; i < nt; i++) printf(" %d", leaf[i]);
  printf("\n");
}

static void cmd_aabb(Mesh *m, int argc, char **argv) {
  int minT = atoi(argv[0]), maxT = atoi(argv[1]);
  float skip = (float)atof(argv[2]);
  int flat = atoi(argv[3]);
  float basis[9] = {1, 0, 0, 0, 1, 0, 0, 0, 1};
  if (argc >= 13) for (int i = 0; i < 9; i++) { uint32_t u = (uint32_t)strtoul(argv[4 + i], 0, 16); memcpy(&basis[i], &u, 4); }
  unsigned char *t = (unsigned char *)calloc(1, 128);
  *(void **)t = VA(g_phys, 0x351F30E8ULL);
  ((fn_aabb_setup)VA(g_phys, 0x35136960ULL))(t, minT, maxT, skip, basis, flat);
  unsigned char *g = fake_mesh(m);
  float cost = ((fn_build)VA(g_phys, 0x35139490ULL))(t, g);
  dump_aabb(t, cost, m->nt);
  print_idx(m);
}

static void cmd_obb(Mesh *m, int argc, char **argv) {
  (void)argc;
  unsigned char *t = (unsigned char *)calloc(1, 72);
  *(void **)t = VA(g_phys, 0x351F3008ULL);
  *(int *)(t + 44) = atoi(argv[0]);
  *(int *)(t + 48) = atoi(argv[1]);
  *(float *)(t + 52) = (float)atof(argv[2]);
  unsigned char *g = fake_mesh(m);
  float cost = ((fn_build)VA(g_phys, 0x35195210ULL))(t, g);
  dump_obb(t, cost, m->nt);
  print_idx(m);
}

typedef struct { void *p; int stride; } Strided;
typedef void (*fn_fit)(Strided *v, Strided *idx, int n, double *axes, double *center);

static void cmd_fit(Mesh *m) {
  Strided v = {m->v, 12}, ix = {m->idx, 4};
  double axes[9] = {0}, c[3] = {0};
  ((fn_fit)VA(g_phys, 0x35195400ULL))(&v, &ix, m->nt, axes, c);
  printf("fit");
  for (int i = 0; i < 9; i++) printf(" %016llx", (unsigned long long)db(axes[i]));
  for (int i = 0; i < 3; i++) printf(" %016llx", (unsigned long long)db(c[i]));
  printf("\n");
}

typedef int (*fn_hull)(Strided *pts, int n, int **out, void *alloc);

static void cmd_hull(Mesh *m) {
  Strided v = {m->v, 12};
  int *out = NULL;
  int n = ((fn_hull)VA(g_phys, 0x35128440ULL))(&v, m->nv, &out, NULL);
  printf("hull %d", n);
  for (int i = 0; i < 3 * n; i++) printf(" %d", out[i]);
  printf("\n");
}

/* CTriMesh: sub_351BF470 (ctor) + sub_351CF1B0 (from mesh data) with flags | 0x20000 (no
 * vertex welding) | 2 (int indices used in place), so the mesh arrays are the file's. */
typedef void *(*fn_tm_ctor)(void *self);
typedef void (*fn_tm_init)(void *self, Strided *v, Strided *idx, void *mats, void *foreign, int nTris,
                           int flags, int minTris, int maxTris, int obbFactorBits);
typedef int (*fn_vtype)(void *);

static void cmd_mesh(Mesh *m, int argc, char **argv) {
  (void)argc;
  int flags = (int)strtoul(argv[0], 0, 0) | 0x20000 | 2;
  int minT = atoi(argv[1]), maxT = atoi(argv[2]);
  float fac = (float)atof(argv[3]);
  uint32_t facb = fb(fac);
  unsigned char *tm = (unsigned char *)calloc(1, 0x260);
  ((fn_tm_ctor)VA(g_phys, 0x351BF470ULL))(tm);
  Strided v = {m->v, 12}, ix = {m->idx, 4};
  ((fn_tm_init)VA(g_phys, 0x351CF1B0ULL))(tm, &v, &ix, NULL, NULL, m->nt, flags, minT, maxT, (int)facb);
  unsigned char *t = *(unsigned char **)(tm + 48);
  printf("mesh flags %08x\n", *(uint32_t *)(tm + 152));
  if (t) {
    int ty = ((fn_vtype)(*(void ***)t)[1])(t);
    if (ty == 2) {
      printf("singlebox");
      for (int i = 0; i < 16; i++) printf(" %08x", *(uint32_t *)(t + 16 + 4 * i));
      printf(" %d %d\n", *(int *)(t + 80), *(int *)(t + 84));
    } else if (ty == 1) dump_aabb(t, 0.0f, m->nt);
    else dump_obb(t, 0.0f, m->nt);
  }
  print_idx(m);
  int *fid = *(int **)(tm + 72);
  printf("foreign");
  for (int i = 0; fid && i < m->nt; i++) printf(" %d", fid[i]);
  printf("\n");
}

/* ---- slot sequences on built trees (the records come from the DLL's own per-caller arena,
 * caller 0, which starts zeroed like the port's BvArena). */
#define ARENA(off) ((unsigned char *)VA(g_phys, 0x35256F80ULL) + (off))
#define VSLOT(obj, n) ((*(void ***)(obj))[n])
typedef void (*fn_bv11)(void *t, void **out, int iNode, int iCaller);
typedef void (*fn_bv9)(void *t, float *R, float *off, float scale, void **out, int iNode, int iCaller);
typedef void (*fn_bv10)(void *t, void **out, float *dir, float len, int iNode, int iCaller);
typedef void (*fn_bv8)(void *t, float *R, float *off, float scale, void **out, float *dir, float len, int iNode, int iCaller);
typedef float (*fn_bv12)(void *t, void *bv);
typedef void (*fn_bv13)(void *t, void *bv, float *dir, float len, void **c0, void **c1, int iCaller);
typedef void (*fn_bv14)(void *t, void *bv, void **c0, void **c1, int iCaller);
typedef void (*fn_bv15)(void *t, float *R, float *off, float scale, void *bv, void **c0, void **c1, int iCaller);
typedef void (*fn_bv16)(void *t, int iCaller);
typedef int (*fn_bv6)(void *t, void *gtest);
typedef void (*fn_bv7)(void *t, void *gtest);
typedef void (*fn_bv25)(void *t, int iTri, void *gtest);
typedef void (*fn_bv5)(void *t);

static float g_R[9], g_off[3] = {1.5f, -2.25f, 0.75f}, g_dir[3] = {0.6f, -0.48f, 0.64f};
static const float g_scale = 1.25f, g_len = 2.5f;

static void init_rot(void) {
  /* rotation by 0.6 rad about (2,1,3)/|.|, computed in double and rounded: any fixed matrix
   * works, the port reads the same bits from the trace header */
  double ax[3] = {2, 1, 3}, n = sqrt(14.0), c = cos(0.6), s = sin(0.6), C = 1 - c;
  for (int i = 0; i < 3; i++) ax[i] /= n;
  double m[9] = {c + ax[0] * ax[0] * C, ax[0] * ax[1] * C - ax[2] * s, ax[0] * ax[2] * C + ax[1] * s,
                 ax[1] * ax[0] * C + ax[2] * s, c + ax[1] * ax[1] * C, ax[1] * ax[2] * C - ax[0] * s,
                 ax[2] * ax[0] * C - ax[1] * s, ax[2] * ax[1] * C + ax[0] * s, c + ax[2] * ax[2] * C};
  for (int i = 0; i < 9; i++) g_R[i] = (float)m[i];
  printf("xf");
  for (int i = 0; i < 9; i++) printf(" %08x", fb(g_R[i]));
  printf("\n");
}

static void prec(const char *tag, void *p, int words) {
  printf("%s", tag);
  for (int i = 0; i < words; i++) printf(" %08x", ((uint32_t *)p)[i]);
  printf("\n");
}

static void counters(void) {
  printf("counters %d %d %d %d\n", *(int *)ARENA(0x359DC), *(int *)ARENA(0x39DE0), *(int *)ARENA(0xC6B0),
         *(int *)ARENA(0xC7B4));
}

/* gtest fields the trees use: +16 map, +24 list, +32 iLast, +36 cap, +44 marked, +388 iCaller */
static void pgtest(const char *tag, unsigned char *g, int nNodes) {
  unsigned char *map = *(unsigned char **)(g + 16);
  unsigned char *list = *(unsigned char **)(g + 24);
  long long mo = map ? (long long)(map - ARENA(0x46B0)) / 4 : -1, lo = list ? (long long)(list - ARENA(0xC6B4)) / 4 : -1;
  printf("%s map %lld list %lld last %d cap %d marked %d", tag, map == g + 32 ? -2LL : mo, lo,
         *(int *)(g + 32), *(int *)(g + 36), *(int *)(g + 44));
  if (map && map != g + 32)
    for (int i = 0; i < ((nNodes - 1) >> 5) + 1; i++) printf(" %08x", ((uint32_t *)map)[i]);
  printf("\n");
}

static void slot_sequence(void *t, int swept_words, int nNodes, int nTris) {
  void *r, *c0, *c1, *d0, *d1;
  ((fn_bv11)VSLOT(t, 11))(t, &r, 0, 0);
  prec("bv11", r, 18);
  ((fn_bv14)VSLOT(t, 14))(t, r, &c0, &c1, 0);
  prec("ch14a", c0, 18);
  prec("ch14b", c1, 18);
  float p0 = ((fn_bv12)VSLOT(t, 12))(t, r), p1 = ((fn_bv12)VSLOT(t, 12))(t, c0), p2 = ((fn_bv12)VSLOT(t, 12))(t, c1);
  printf("prio %08x %08x %08x\n", fb(p0), fb(p1), fb(p2));
  if (p1 > 0) {
    ((fn_bv14)VSLOT(t, 14))(t, c0, &d0, &d1, 0);
    prec("ch14c", d0, 18);
    prec("ch14d", d1, 18);
    ((fn_bv16)VSLOT(t, 16))(t, 0);
  }
  ((fn_bv16)VSLOT(t, 16))(t, 0);
  counters();
  ((fn_bv9)VSLOT(t, 9))(t, g_R, g_off, g_scale, &r, 1, 0);
  prec("bv9", r, 18);
  ((fn_bv15)VSLOT(t, 15))(t, g_R, g_off, g_scale, r, &c0, &c1, 0);
  prec("ch15a", c0, 18);
  prec("ch15b", c1, 18);
  if (((fn_bv12)VSLOT(t, 12))(t, c1) > 0) {
    ((fn_bv15)VSLOT(t, 15))(t, g_R, g_off, g_scale, c1, &d0, &d1, 0);
    prec("ch15c", d0, 18);
    prec("ch15d", d1, 18);
    ((fn_bv16)VSLOT(t, 16))(t, 0);
  }
  ((fn_bv16)VSLOT(t, 16))(t, 0);
  counters();
  ((fn_bv10)VSLOT(t, 10))(t, &r, g_dir, g_len, 0, 0);
  prec("bv10", r, swept_words);
  ((fn_bv13)VSLOT(t, 13))(t, r, g_dir, g_len, &c0, &c1, 0);
  prec("ch13a", c0, swept_words);
  prec("ch13b", c1, swept_words);
  if (((fn_bv12)VSLOT(t, 12))(t, c0) > 0) {
    ((fn_bv13)VSLOT(t, 13))(t, c0, g_dir, g_len, &d0, &d1, 0);
    prec("ch13c", d0, swept_words);
    prec("ch13d", d1, swept_words);
    ((fn_bv16)VSLOT(t, 17))(t, 0);
  }
  ((fn_bv16)VSLOT(t, 17))(t, 0);
  counters();
  /* used-node marking */
  unsigned char *g = (unsigned char *)calloc(1, 392);
  *(int *)(g + 388) = 0;
  int ok = ((fn_bv6)VSLOT(t, 6))(t, g);
  printf("prep %d\n", ok);
  pgtest("g6", g, nNodes);
  counters();
  for (int i = 0; i < nTris; i += 3) ((fn_bv25)VSLOT(t, 25))(t, i, g);
  pgtest("g25", g, nNodes);
  ((fn_bv7)VSLOT(t, 7))(t, g);
  pgtest("g7", g, nNodes);
  printf("skip %08x\n", fb(((float (*)(void *))VSLOT(t, 19))(t)));
}

static void cmd_slots(Mesh *m, int argc, char **argv) {
  (void)argc;
  init_rot();
  const char *kind = argv[0];
  int minT = atoi(argv[1]), maxT = atoi(argv[2]);
  float skip = (float)atof(argv[3]);
  unsigned char *g = fake_mesh(m);
  if (!strcmp(kind, "aabb")) {
    float basis[9] = {1, 0, 0, 0, 1, 0, 0, 0, 1};
    unsigned char *t = (unsigned char *)calloc(1, 128);
    *(void **)t = VA(g_phys, 0x351F30E8ULL);
    ((fn_aabb_setup)VA(g_phys, 0x35136960ULL))(t, minT, maxT, skip, basis, 0);
    ((fn_build)VA(g_phys, 0x35139490ULL))(t, g);
    slot_sequence(t, 34, *(int *)(t + 24), m->nt);
  } else {
    unsigned char *t = (unsigned char *)calloc(1, 72);
    *(void **)t = VA(g_phys, 0x351F3008ULL);
    *(int *)(t + 44) = minT;
    *(int *)(t + 48) = maxT;
    *(float *)(t + 52) = skip;
    ((fn_build)VA(g_phys, 0x35195210ULL))(t, g);
    slot_sequence(t, 18, *(int *)(t + 24), m->nt);
    ((fn_bv5)VSLOT(t, 5))(t);
    printf("convex %08x\n", *(uint32_t *)(t + 52));
  }
}

/* CSingleBoxTree and CRayBV slots on a fixed box / ray. */
static void cmd_prims(void) {
  init_rot();
  unsigned char *t = (unsigned char *)calloc(1, 88);
  *(void **)t = VA(g_phys, 0x351EE4A8ULL);
  float box[16];
  memcpy(box, g_R, 36);
  ((uint32_t *)box)[9] = 1;
  box[10] = 0.5f; box[11] = -1.0f; box[12] = 2.0f; box[13] = 0.75f; box[14] = 1.5f; box[15] = 0.25f;
  memcpy(t + 16, box, 64);
  *(int *)(t + 80) = 1;
  void *r;
  ((fn_bv11)VSLOT(t, 11))(t, &r, 0, 0);
  prec("sb11", r, 18);
  ((fn_bv9)VSLOT(t, 9))(t, g_R, g_off, g_scale, &r, 0, 0);
  prec("sb9", r, 18);
  ((fn_bv10)VSLOT(t, 10))(t, &r, g_dir, g_len, 0, 0);
  prec("sb10", r, 18);
  ((fn_bv8)VSLOT(t, 8))(t, g_R, g_off, g_scale, &r, g_dir, g_len, 0, 0);
  prec("sb8", r, 18);
  counters();
  unsigned char *g = (unsigned char *)calloc(1, 392);
  ((fn_bv6)VSLOT(t, 6))(t, g);
  ((fn_bv25)VSLOT(t, 25))(t, 0, g);
  pgtest("sbg", g, 1);
  ((fn_bv5)VSLOT(t, 5))(t);
  ((fn_bv25)VSLOT(t, 25))(t, 0, g);
  pgtest("sbg5", g, 1);
  float ray[6] = {1.0f, -0.5f, 2.0f, 0.3f, 4.0f, -1.25f};
  unsigned char *rb = (unsigned char *)calloc(1, 24);
  *(void **)rb = VA(g_phys, 0x351EDEC8ULL);
  *(float **)(rb + 16) = ray;
  ((fn_bv9)VSLOT(rb, 9))(rb, g_R, g_off, g_scale, &r, 0, 0);
  prec("ray9", r, 8);
  ((fn_bv11)VSLOT(rb, 11))(rb, &r, 0, 0);
  prec("ray11", r, 8);
}

/* sub_35135490 on generated boxes. */
typedef void (*fn_sweep)(float *box, float *dir, float len, float *out);
static uint32_t g_seed;
static float frnd(void) { g_seed = g_seed * 1664525u + 1013904223u; return (float)(g_seed >> 8) / 16777216.0f; }

static void cmd_sweep(int n, unsigned seed) {
  g_seed = seed;
  for (int k = 0; k < n; k++) {
    float box[16], dir[3], out[16];
    /* a random orthonormal-ish basis: perturbed rotation rows, normalised in float */
    for (int i = 0; i < 9; i++) box[i] = frnd() * 2 - 1;
    ((uint32_t *)box)[9] = 1;
    for (int i = 10; i < 13; i++) box[i] = frnd() * 10 - 5;
    for (int i = 13; i < 16; i++) box[i] = frnd() * 3;
    float l = 0;
    for (int i = 0; i < 3; i++) { dir[i] = frnd() * 2 - 1; l += dir[i] * dir[i]; }
    l = 1.0f / sqrtf(l);
    for (int i = 0; i < 3; i++) dir[i] *= l;
    float len = frnd() * 8 - 2;
    if (k % 7 == 3) { dir[0] = box[3]; dir[1] = box[4]; dir[2] = box[5]; } /* degenerate case */
    ((fn_sweep)VA(g_phys, 0x35135490ULL))(box, dir, len, out);
    printf("in");
    for (int i = 0; i < 16; i++) printf(" %08x", fb(box[i]));
    printf(" %08x %08x %08x %08x out", fb(dir[0]), fb(dir[1]), fb(dir[2]), fb(len));
    for (int i = 0; i < 16; i++) printf(" %08x", ((uint32_t *)out)[i]);
    printf("\n");
  }
}

int main(int argc, char **argv) {
  const char *rt = getenv("AAEMU_PHYS_RT");
  if (!rt) rt = "D:\\aa-re\\cryphysics\\harness\\rt";
  int a = 1;
  if (a + 1 < argc && !strcmp(argv[a], "--rt")) { rt = argv[a + 1]; a += 2; }
  if (a >= argc || (a + 1 >= argc && strcmp(argv[a], "prims"))) { fprintf(stderr, "usage: p1b_probe [--rt DIR] aabb|obb|fit|hull MESH [args]\n"); return 1; }
  char dll[MAX_PATH];
  snprintf(dll, sizeof dll, "%s\\cryphysics_dedicate.dll", rt);
  SetDllDirectoryA(rt);
  g_phys = LoadLibraryA(dll);
  if (!g_phys) { printf("ERROR LoadLibrary %s failed (%lu)\n", dll, GetLastError()); return 1; }
  const char *cmd = argv[a];
  if (!strcmp(cmd, "prims")) { cmd_prims(); fflush(stdout); return 0; }
  if (!strcmp(cmd, "sweep")) { cmd_sweep(atoi(argv[a + 1]), (unsigned)strtoul(argv[a + 2], 0, 0)); fflush(stdout); return 0; }
  Mesh m = load_mesh(argv[a + 1]);
  if (!strcmp(cmd, "aabb")) cmd_aabb(&m, argc - a - 2, argv + a + 2);
  else if (!strcmp(cmd, "obb")) cmd_obb(&m, argc - a - 2, argv + a + 2);
  else if (!strcmp(cmd, "fit")) cmd_fit(&m);
  else if (!strcmp(cmd, "hull")) cmd_hull(&m);
  else if (!strcmp(cmd, "mesh")) cmd_mesh(&m, argc - a - 2, argv + a + 2);
  else if (!strcmp(cmd, "slots")) cmd_slots(&m, argc - a - 2, argv + a + 2);
  else { printf("ERROR unknown command %s\n", cmd); return 1; }
  fflush(stdout);
  return 0;
}
