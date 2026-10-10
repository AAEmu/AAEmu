/* p1c_core_inside_probe.c: P1c-core-inside probe. Runs CTriMesh slot 22 PrepareForRayTest
 * (0x351D7C40, ray grid builder sub_351C8B10), slot 61 PointInsideStatus(pt, Triangle*)
 * (0x351C8600) and slot 8 PointInsideStatus(pt) (0x351DB380) of the real DLL.
 *
 *   p1c_core_inside_probe.exe [--rt DIR] <job file>
 *
 * Job file (binary, little endian): u32 count, then per case
 *   u32 kind
 *     kind 0 (synthetic): u32 flags, u32 nv, u32 nt, f32 v[3 nv], i32 idx[3 nt]
 *                         -> DLL constructor (ctor 0x351BF470 + 0x351CF1B0, minTris 2,
 *                            maxTris 4, obb 1)
 *     kind 1 (CGF blob):  u32 len, u8 blob[len] (version, 64-byte header, geom type, mesh)
 *                         -> ctor + slot 26 Load on the bytes after the geom type
 *   u32 prep (0: none, 1: call slot 22 first), u32 raylen bits, u32 npts, f32 pts[3 npts]
 *
 * Output per case:
 *   case K
 *   <p1c_mesh_dump block>                          (before any of the slots ran)
 *   prep RET ready N                               (only with prep = 1)
 *   pt I R61 <13 out words> R61NULL R8             (out pre-filled with cdcdcdcd)
 *   grid ready N
 *   layer K rec <20 words: +0..+36, +40..+84>      (for each built layer K < N)
 *   cells K <n0*n1+1 ints>
 *   tris K <cells[n0*n1] ints>
 */
#include "p1c_common.h"

typedef char (*fn22)(void *, float);
typedef int (*fn61)(void *, float *, float *);
typedef int (*fn8)(void *, float *);

static const unsigned char *g_p;
static uint32_t rd32(void) { uint32_t u; memcpy(&u, g_p, 4); g_p += 4; return u; }

int main(int argc, char **argv) {
  p1c_init(&argc, &argv);
  if (argc < 2) { printf("usage: p1c_core_inside_probe [--rt DIR] job\n"); return 1; }
  long len;
  g_p = p1c_read_file(argv[1], &len);
  uint32_t count = rd32();
  for (uint32_t c = 0; c < count; c++) {
    unsigned char *tm;
    uint32_t kind = rd32();
    printf("case %u\n", c);
    if (kind == 0) {
      int flags = (int)rd32(), nv = (int)rd32(), nt = (int)rd32();
      float *v = (float *)malloc(sizeof(float) * 3 * (nv + 1));
      int *idx = (int *)malloc(sizeof(int) * 3 * (nt + 1));
      memcpy(v, g_p, 12 * (size_t)nv); g_p += 12 * (size_t)nv;
      memcpy(idx, g_p, 12 * (size_t)nt); g_p += 12 * (size_t)nt;
      tm = p1c_mesh_new(v, idx, 4, NULL, NULL, nt, flags, 2, 4, 1.0f);
    } else {
      uint32_t bl = rd32();
      P1cStream *s = p1c_stream_read(g_p + 72, (int)bl - 72);
      g_p += bl;
      P1cStrided vtx = {NULL, 12}, idx = {NULL, 6};
      tm = p1c_mesh_load(s, &vtx, &idx, NULL);
    }
    p1c_mesh_dump(tm);
    uint32_t prep = rd32(), rl = rd32(), np = rd32();
    if (prep) {
      char r = ((fn22)VSLOT(tm, 22))(tm, bf(rl));
      printf("prep %d ready %d\n", (int)(unsigned char)r, *(int *)(tm + 476));
    }
    for (uint32_t i = 0; i < np; i++) {
      __declspec(align(16)) float pt[4], out[16];
      memcpy(pt, g_p, 12); g_p += 12;
      memset(out, 0xCD, sizeof out);
      int r61 = ((fn61)VSLOT(tm, 61))(tm, pt, out);
      int r61n = ((fn61)VSLOT(tm, 61))(tm, pt, NULL);
      int r8 = ((fn8)VSLOT(tm, 8))(tm, pt);
      printf("pt %u %d", i, r61);
      for (int k = 0; k < 13; k++) printf(" %08x", fb(out[k]));
      printf(" %d %d\n", r61n, r8);
    }
    int ready = *(int *)(tm + 476);
    printf("grid ready %d\n", ready);
    for (int k = 0; k < ready && k < 2; k++) {
      uint32_t *rec = (uint32_t *)(tm + 224 + 84 * k);
      printf("layer %d rec", k);
      for (int w = 0; w < 21; w++)
        if (w != 9) printf(" %08x", rec[w]);
      printf("\n");
      int *cells = *(int **)(tm + 176 + 8 * k);
      int *tris = *(int **)(tm + 200 + 8 * k);
      int n = (int)rec[17] * (int)rec[18];
      printf("cells %d", k);
      for (int i = 0; i <= n; i++) printf(" %d", cells[i]);
      printf("\n");
      printf("tris %d", k);
      for (int i = 0; i < cells[n]; i++) printf(" %d", tris[i]);
      printf("\n");
    }
    fflush(stdout);
  }
  return 0;
}
