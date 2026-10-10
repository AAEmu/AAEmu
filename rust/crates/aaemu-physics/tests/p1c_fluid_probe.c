/* p1c_fluid_probe.c: P1c-fluid probe. Runs CTriMesh slots 11 (FindClosestPoint 0x351C6780),
 * 12 (CalcVolumetricPressure 0x351CBE20), 13 (CalculateBuoyancy 0x351DA620), 14
 * (CalculateMediumResistance 0x351CD0C0) and CHeightfield slots 11..14 (0x35155CB0, 0x35154CB0,
 * and the inherited CTriMesh 13 / 14) of the real DLL on the queries of a job file.
 *
 *   p1c_fluid_probe.exe [--rt DIR] <job file>
 *
 * Job file: one command per line, every float as its raw IEEE bits in hex.
 *   mesh NV NT FLAGS / v <3*NV words> / i <3*NT ints>   build a DLL CTriMesh (ctor 0x351BF470 +
 *                                                       0x351CF1B0, minTris 2, maxTris 4, obb 1),
 *                                                       print its dump (p1c_mesh_dump, tree omitted)
 *   hf NX NY SX SY HSCALE MODE                          create a CHeightfield through
 *                                                       CGeomManager::CreatePrimitive(2); prints
 *                                                       "hfobj <30 prim words of +608> nt <+120>"
 *   b13 <plane 6> <gwd 22>                       -> r13 <V> <center 3>
 *   b14 <plane 6> <gwd 22> <dP 3> <dL 3>         -> r14 <dP 3> <dL 3>        (incoming values)
 *   b12 <gwd 22> <epi 3> <k> <rmin> <com 3> <P 3> <L 3>   -> r12 <P 3> <L 3>
 *   c11 <gwd 22> <iPrim> <iFeature> <p0 3> <p1 3> <nMaxIters> <ptres 6>
 *                                                -> r11 <ret> <iPrim> <iFeature> <ptres 6>
 * The b / c queries go to the current object (the last mesh or hf). gwd is geom_world_data:
 * offset 3, R 9 (row major), scale, v 3, w 3, centerOfMass 3.
 *
 * Heightfield modes (the Rust tests implement the same callbacks):
 *   0 flat 0.5   1 ((ix*7+iy*13) mod 5)*0.75-1   2 hashed bumps   3 hashed bumps with holes
 *   (surface type 0x8000 = hole where (ix*3+iy*5) mod 7 == 0) */
#include "p1c_common.h"

static int g_hf_mode;
static int p1c_mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
static float hf_height(int ix, int iy) {
  unsigned u;
  switch (g_hf_mode) {
  case 1: return (float)p1c_mod(ix * 7 + iy * 13, 5) * 0.75f - 1.0f;
  case 2: case 3:
    u = ((unsigned)ix * 73856093u) ^ ((unsigned)iy * 19349663u);
    return (float)((u >> 8) & 1023u) * 0.01f - 5.0f;
  default: return 0.5f;
  }
}
static unsigned short hf_surf(int ix, int iy) {
  if (g_hf_mode == 3 && p1c_mod(ix * 3 + iy * 5, 7) == 0) return 0x8000;
  return 0;
}

static char *next_tok(void) { return strtok(NULL, " \t\r\n"); }
static uint32_t tw(void) {
  char *t = next_tok();
  if (!t) { printf("ERROR short line\n"); exit(2); }
  return (uint32_t)strtoul(t, 0, 16);
}
static int ti(void) {
  char *t = next_tok();
  if (!t) { printf("ERROR short line\n"); exit(2); }
  return (int)strtol(t, 0, 10);
}
static void tws(float *f, int n) { for (int i = 0; i < n; i++) f[i] = bf(tw()); }
static void out_words(const char *tag, const float *f, int n) {
  printf("%s", tag);
  for (int i = 0; i < n; i++) printf(" %08x", fb(f[i]));
}

typedef float (*fn13)(void *, float *, float *, float *);
typedef void (*fn14)(void *, float *, float *, float *, float *);
typedef void (*fn12)(void *, float *, float *, float, float, float *, float *, float *);
typedef int (*fn11)(void *, float *, int *, int *, float *, float *, float *, int);

int main(int argc, char **argv) {
  p1c_init(&argc, &argv);
  if (argc < 2) { printf("usage: p1c_fluid_probe [--rt DIR] job\n"); return 1; }
  void *world = ((void *(*)(void *))GetProcAddress(g_phys, "CreatePhysicalWorld"))(NULL);
  if (!world) { printf("ERROR CreatePhysicalWorld\n"); return 1; }
  p1c_install_env();
  void *gm = ((void *(*)(void *))VSLOT(world, 9))(world);
  long len;
  char *job = (char *)p1c_read_file(argv[1], &len);
  job[len] = 0;
  void *obj = NULL;
  char *line = job;
  while (line && *line) {
    char *nl = strchr(line, '\n');
    if (nl) *nl = 0;
    char *cmd = strtok(line, " \t\r");
    if (!cmd) {
    } else if (!strcmp(cmd, "mesh")) {
      int nv = ti(), nt = ti();
      int flags = (int)strtoul(next_tok(), 0, 16);
      line = nl + 1; nl = strchr(line, '\n'); if (nl) *nl = 0;
      strtok(line, " \t\r");
      float *v = (float *)malloc(sizeof(float) * 3 * (nv + 1));
      tws(v, 3 * nv);
      line = nl + 1; nl = strchr(line, '\n'); if (nl) *nl = 0;
      strtok(line, " \t\r");
      int *idx = (int *)malloc(sizeof(int) * 3 * (nt + 1));
      for (int k = 0; k < 3 * nt; k++) idx[k] = ti();
      obj = p1c_mesh_new(v, idx, 4, NULL, NULL, nt, flags, 2, 4, 1.0f);
      /* the tree is not printed: these slots do not use it, and an OBB tree's saved bytes
       * contain a heap pointer (not reproducible across runs) */
      void *tree = *(void **)((unsigned char *)obj + 48);
      *(void **)((unsigned char *)obj + 48) = NULL;
      p1c_mesh_dump((unsigned char *)obj);
      *(void **)((unsigned char *)obj + 48) = tree;
    } else if (!strcmp(cmd, "hf")) {
      __declspec(align(16)) unsigned char prim[128];
      memset(prim, 0, sizeof prim);
      float *f = (float *)prim;
      f[0] = f[4] = f[8] = 1;
      int nx = ti(), ny = ti();
      float sx = bf(tw()), sy = bf(tw()), hs = bf(tw());
      g_hf_mode = ti();
      f[13] = sx; f[14] = sy; f[15] = 1.0f / sx; f[16] = 1.0f / sy;
      *(int *)(prim + 68) = nx; *(int *)(prim + 72) = ny;
      *(int *)(prim + 76) = 1; *(int *)(prim + 80) = nx;
      f[21] = hs;
      *(uint16_t *)(prim + 88) = 0x8000;
      *(uint16_t *)(prim + 90) = 0xFFFF;
      *(int *)(prim + 92) = 1;
      *(int *)(prim + 96) = 15;
      *(void **)(prim + 104) = (void *)hf_height;
      *(void **)(prim + 112) = (void *)hf_surf;
      obj = ((void *(*)(void *, int, void *))VSLOT(gm, 7))(gm, 2, prim);
      if (!obj) { printf("ERROR CreatePrimitive\n"); return 1; }
      prw("hfobj", (unsigned char *)obj + 608, 26);
      printf("hfnt %d %d\n", *(int *)((unsigned char *)obj + 120), *(int *)((unsigned char *)obj + 124));
    } else if (!strcmp(cmd, "b13")) {
      __declspec(align(16)) float pl[8], g[24], c[4] = {0};
      tws(pl, 6); tws(g, 22);
      float r = ((fn13)VSLOT(obj, 13))(obj, pl, g, c);
      out_words("r13", &r, 1); out_words("", c, 3); printf("\n");
    } else if (!strcmp(cmd, "b14")) {
      __declspec(align(16)) float pl[8], g[24], o[8];
      tws(pl, 6); tws(g, 22); tws(o, 6);
      ((fn14)VSLOT(obj, 14))(obj, pl, g, o, o + 3);
      out_words("r14", o, 6); printf("\n");
    } else if (!strcmp(cmd, "b12")) {
      __declspec(align(16)) float g[24], in[12], o[8];
      tws(g, 22); tws(in, 8); tws(o, 6);
      ((fn12)VSLOT(obj, 12))(obj, g, in, in[3], in[4], in + 5, o, o + 3);
      out_words("r12", o, 6); printf("\n");
    } else if (!strcmp(cmd, "c11")) {
      __declspec(align(16)) float g[24], p0[4], p1[4], res[8];
      tws(g, 22);
      int ip = ti(), ifeat = ti();
      tws(p0, 3); tws(p1, 3);
      int nmax = ti();
      tws(res, 6);
      int r = ((fn11)VSLOT(obj, 11))(obj, g, &ip, &ifeat, p0, p1, res, nmax);
      printf("r11 %d %d %d", r, ip, ifeat); out_words("", res, 6); printf("\n");
    } else {
      printf("ERROR unknown command %s\n", cmd);
      return 2;
    }
    line = nl ? nl + 1 : NULL;
  }
  fflush(stdout);
  return 0;
}
