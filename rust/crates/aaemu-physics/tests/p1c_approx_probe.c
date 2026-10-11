/* p1c_approx_probe.c: CreateMesh's primitive approximation of the real x64
 * cryphysics_dedicate.dll (10.0.2.13), for tests/p1c_approx.rs.
 *
 *   p1c_approx_probe [--rt DIR] <cases.bin>
 *
 * cases.bin: u32 count, then per case u32 nv, u32 nt, u32 flags, f32 tol, u32 hasMats,
 * f32 v[3*nv], u16 idx[3*nt], i8 mats[nt] (if hasMats).
 *
 * Per case it calls CGeomManager slot 6 (0x35098490; `this` is unused) and prints
 *   case <k> tol <bits> ret <type> [primitive words]   (box 16, cylinder / capsule 8, sphere 4)
 * then sub_350975D0's principal axes and centre (double bits):
 *   axes <9 axes> <3 centre>
 */
#include "p1c_common.h"

typedef struct { void *p; int stride; int pad; } Str;
typedef int (*fn_slot6)(void *self, Str *v, Str *i, signed char *mats, int nTris, int flags, float tol,
                        void **prim);
typedef void (*fn_axes)(Str *v, Str *i, int nTris, double *axes, double *center);

int main(int argc, char **argv) {
  p1c_init(&argc, &argv);
  if (argc < 2) { printf("usage: p1c_approx_probe [--rt DIR] cases.bin\n"); return 2; }
  long len;
  unsigned char *b = p1c_read_file(argv[1], &len), *p = b;
  fn_slot6 slot6 = (fn_slot6)VA(0x35098490ULL);
  fn_axes axesf = (fn_axes)VA(0x350975D0ULL);
  uint32_t count;
  memcpy(&count, p, 4);
  p += 4;
  for (uint32_t k = 0; k < count; k++) {
    uint32_t h[5];
    memcpy(h, p, 20);
    p += 20;
    uint32_t nv = h[0], nt = h[1], flags = h[2], has = h[4];
    float tol = bf(h[3]);
    float *v = (float *)malloc(12 * (size_t)nv + 16);
    memcpy(v, p, 12 * (size_t)nv);
    p += 12 * (size_t)nv;
    uint16_t *idx = (uint16_t *)malloc(6 * (size_t)nt + 16);
    memcpy(idx, p, 6 * (size_t)nt);
    p += 6 * (size_t)nt;
    signed char *mats = 0;
    if (has) {
      mats = (signed char *)malloc((size_t)nt + 16);
      memcpy(mats, p, nt);
      p += nt;
    }
    Str sv = {v, 12, 0}, si = {idx, 2, 0};
    void *prim = 0;
    int ret = slot6(0, &sv, &si, mats, (int)nt, (int)flags, tol, &prim);
    printf("case %u tol %08x ret %d", k, h[3], ret);
    int words = ret == 0 ? 16 : ret == 5 || ret == 6 ? 8 : ret == 4 ? 4 : 0;
    for (int w = 0; w < words; w++) printf(" %08x", ((uint32_t *)prim)[w]);
    printf("\n");
    double ax[9], c[3];
    Str sv2 = {v, 12, 0}, si2 = {idx, 2, 0};
    axesf(&sv2, &si2, (int)nt, ax, c);
    printf("axes");
    for (int w = 0; w < 9; w++) printf(" %016llx", (unsigned long long)db(ax[w]));
    for (int w = 0; w < 3; w++) printf(" %016llx", (unsigned long long)db(c[w]));
    printf("\n");
    free(v);
    free(idx);
    free(mats);
  }
  fflush(stdout);
  return 0;
}
