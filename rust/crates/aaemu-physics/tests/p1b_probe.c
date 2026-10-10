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
 * The runtime directory (--rt, %AAEMU_PHYS_RT%, default D:\aa-re\cryphysics\harness\rt) is the
 * one physoracle uses. */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

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

int main(int argc, char **argv) {
  const char *rt = getenv("AAEMU_PHYS_RT");
  if (!rt) rt = "D:\\aa-re\\cryphysics\\harness\\rt";
  int a = 1;
  if (a + 1 < argc && !strcmp(argv[a], "--rt")) { rt = argv[a + 1]; a += 2; }
  if (a + 1 >= argc) { fprintf(stderr, "usage: p1b_probe [--rt DIR] aabb|obb|fit|hull MESH [args]\n"); return 1; }
  char dll[MAX_PATH];
  snprintf(dll, sizeof dll, "%s\\cryphysics_dedicate.dll", rt);
  SetDllDirectoryA(rt);
  g_phys = LoadLibraryA(dll);
  if (!g_phys) { printf("ERROR LoadLibrary %s failed (%lu)\n", dll, GetLastError()); return 1; }
  Mesh m = load_mesh(argv[a + 1]);
  const char *cmd = argv[a];
  if (!strcmp(cmd, "aabb")) cmd_aabb(&m, argc - a - 2, argv + a + 2);
  else if (!strcmp(cmd, "obb")) cmd_obb(&m, argc - a - 2, argv + a + 2);
  else if (!strcmp(cmd, "fit")) cmd_fit(&m);
  else if (!strcmp(cmd, "hull")) cmd_hull(&m);
  else if (!strcmp(cmd, "mesh")) cmd_mesh(&m, argc - a - 2, argv + a + 2);
  else { printf("ERROR unknown command %s\n", cmd); return 1; }
  fflush(stdout);
  return 0;
}
