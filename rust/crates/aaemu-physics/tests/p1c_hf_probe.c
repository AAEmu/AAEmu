/* p1c_hf_probe.c: drives the DLL's CHeightfield (ctor 0x35103E50 + SetData 0x35154F40), its
 * CHeightfieldBV slots and the ray branch of slot 10 on generated grids and prints every input
 * and output as raw bits. tests/p1c_hf.rs replays each "in | out" line with the port.
 *
 *   p1c_hf_probe.exe [--rt DIR] run <seed> <ngrids>
 */
#include "p1c_common.h"

#define ARENA(c, off) ((unsigned char *)VA(0x35256F80ULL) + (size_t)(c)*0x39FB8 + (off))

static uint64_t g_rng;
static uint32_t rnd(void) {
  g_rng ^= g_rng << 13;
  g_rng ^= g_rng >> 7;
  g_rng ^= g_rng << 17;
  return (uint32_t)(g_rng >> 16);
}
static float frand(float a, float b) { return a + (b - a) * (float)(rnd() & 0xFFFFFF) / 16777216.0f; }
static int irand(int a, int b) { return a + (int)(rnd() % (uint32_t)(b - a + 1)); }

/* ---- grid tables (callbacks clamp into [-3, n+3]) ---- */
#define PAD 3
static int g_nx, g_ny, g_tw, g_th;
static float *g_H;
static uint16_t *g_S;
static int tidx(int x, int y) {
  if (x < -PAD) x = -PAD;
  if (y < -PAD) y = -PAD;
  if (x > g_nx + PAD) x = g_nx + PAD;
  if (y > g_ny + PAD) y = g_ny + PAD;
  return (y + PAD) * g_tw + (x + PAD);
}
static float cb_height(int x, int y) { return g_H[tidx(x, y)]; }
static uint16_t cb_surf(int x, int y) { return g_S[tidx(x, y)]; }

typedef struct {
  float basis[9];
  int oriented;
  float origin[3];
  float step[2], stepr[2];
  int size[2], stride[2];
  float hs;
  uint16_t typemask, heightmask;
  int typehole, typepower;
  void *getHeight, *getSurf;
} HfPrim; /* 120 bytes */

typedef struct { void **vt; float box[16]; } FakeGeom;
static void fake_bbox(FakeGeom *g, float *out) { memcpy(out, g->box, 64); }
static void *g_fake_vt[64];

static void pw(const float *p, int n) { for (int i = 0; i < n; i++) printf(" %08x", fb(p[i])); }
static void pwi(const void *p, int n) { for (int i = 0; i < n; i++) printf(" %08x", ((const uint32_t *)p)[i]); }

static void rand_rot(float *m) {
  if (rnd() % 4 == 0) {
    float id[9] = {1, 0, 0, 0, 1, 0, 0, 0, 1};
    memcpy(m, id, sizeof id);
    return;
  }
  double q[4], n = 0;
  for (int i = 0; i < 4; i++) { q[i] = frand(-1, 1); n += q[i] * q[i]; }
  n = sqrt(n);
  for (int i = 0; i < 4; i++) q[i] /= n;
  double x = q[0], y = q[1], z = q[2], w = q[3];
  double d[9] = {1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                 2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                 2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)};
  for (int i = 0; i < 9; i++) m[i] = (float)d[i];
}

static float g_hmin, g_hmax;

static void make_grid(unsigned char *hf, HfPrim *src) {
  static const float steps[] = {0.5f, 1.0f, 2.0f, 4.0f, 0.25f};
  static const uint16_t masks[] = {0x0F00, 0x00F0, 0x0007, 0xFFFF, 0x8000, 0x0380};
  g_nx = irand(2, 24);
  g_ny = irand(2, 24);
  if (rnd() % 5 == 0) { g_nx = irand(30, 60); g_ny = irand(2, 8); }
  g_tw = g_nx + 2 * PAD + 1;
  g_th = g_ny + 2 * PAD + 1;
  free(g_H);
  free(g_S);
  g_H = (float *)malloc(sizeof(float) * g_tw * g_th);
  g_S = (uint16_t *)malloc(2 * g_tw * g_th);
  int kind = irand(0, 3);
  float c0 = frand(-3, 3), ka = frand(-1, 1), kb = frand(-1, 1), amp = frand(0.2f, 3);
  for (int y = -PAD; y <= g_ny + PAD; y++)
    for (int x = -PAD; x <= g_nx + PAD; x++) {
      float h;
      switch (kind) {
      case 0: h = c0; break;
      case 1: h = c0 + ka * x + kb * y; break;
      case 2: h = c0 + amp * sinf(0.7f * x) * cosf(0.45f * y); break;
      default: h = frand(-2, 4); break;
      }
      g_H[(y + PAD) * g_tw + x + PAD] = h;
      g_S[(y + PAD) * g_tw + x + PAD] = (uint16_t)rnd();
    }
  memset(src, 0, sizeof *src);
  rand_rot(src->basis);
  src->oriented = 7;
  src->origin[0] = frand(-9, 9);
  src->origin[1] = frand(-9, 9);
  src->origin[2] = frand(-9, 9);
  src->step[0] = rnd() % 3 ? steps[rnd() % 5] : frand(0.3f, 3);
  src->step[1] = rnd() % 3 ? src->step[0] : frand(0.3f, 3);
  src->stepr[0] = 99;
  src->stepr[1] = 99;
  src->size[0] = g_nx;
  src->size[1] = g_ny;
  src->stride[0] = (int)rnd();
  src->stride[1] = (int)rnd();
  src->hs = rnd() % 2 ? 1.0f : frand(0.2f, 3);
  src->typemask = masks[rnd() % 6];
  src->heightmask = (uint16_t)rnd();
  int tz = 0;
  while (!((src->typemask >> tz) & 1)) tz++;
  int maxv = src->typemask >> tz;
  src->typehole = rnd() % 4 == 0 ? -1 : (int)(rnd() % (uint32_t)(maxv + 1 < 9 ? maxv + 1 : 9));
  src->typepower = (int)rnd();
  src->getHeight = (void *)cb_height;
  src->getSurf = (void *)cb_surf;
  printf("grid %d %d", g_nx, g_ny);
  pw(src->basis, 9);
  printf(" %d", src->oriented);
  pw(src->origin, 3);
  pw(src->step, 2);
  pw(src->stepr, 2);
  printf(" %d %d", src->stride[0], src->stride[1]);
  pw(&src->hs, 1);
  printf(" %u %u %d %d\n", src->typemask, src->heightmask, src->typehole, src->typepower);
  printf("H");
  pw(g_H, g_tw * g_th);
  printf("\nS");
  for (int i = 0; i < g_tw * g_th; i++) printf(" %u", g_S[i]);
  printf("\n");
  g_hmin = 1e30f;
  g_hmax = -1e30f;
  for (int i = 0; i < g_tw * g_th; i++) {
    float h = g_H[i] * src->hs;
    if (h < g_hmin) g_hmin = h;
    if (h > g_hmax) g_hmax = h;
  }
  ((void *(*)(void *))VA(0x35103E50ULL))(hf);
  ((void *(*)(void *, void *))VA(0x35154F40ULL))(hf, src);
  memset(*(void **)(hf + 776), 0, 4 * ((*(int *)(hf + 796) >> 5) + 1)); /* port zero-inits */
}

static void dump_create(unsigned char *hf, HfPrim *src) {
  printf("create |");
  pwi(hf + 608, 25);
  pw(src->stepr, 2);
  pwi(hf + 16, 2);
  pwi(hf + 784, 4);
  printf(" %d %d\n", *(int *)(hf + 120), *(int *)(hf + 124));
}

static void pis_cases(unsigned char *hf, int n) {
  typedef int (*fn8)(void *, float *);
  for (int i = 0; i < n; i++) {
    float p[3];
    p[0] = frand(-1.5f, g_nx + 1.5f) * *(float *)(hf + 660);
    p[1] = frand(-1.5f, g_ny + 1.5f) * *(float *)(hf + 664);
    p[2] = frand(g_hmin - 1, g_hmax + 1);
    if (rnd() % 8 == 0) { p[0] = (float)irand(0, g_nx) * *(float *)(hf + 660); }
    int r = ((fn8)VSLOT(hf, 8))(hf, p);
    printf("pis");
    pw(p, 3);
    printf(" | %d\n", r);
  }
}

static void rand_box(float *b, float sx, float sy) {
  if (rnd() % 3) {
    float id[9] = {1, 0, 0, 0, 1, 0, 0, 0, 1};
    memcpy(b, id, 36);
  } else
    rand_rot(b);
  ((int *)b)[9] = rnd() % 2;
  float big = rnd() % 10 == 0 ? 3.0f : 1.0f;
  b[10] = frand(-2, g_nx + 2) * sx;
  b[11] = frand(-2, g_ny + 2) * sy;
  b[12] = frand(g_hmin - 3, g_hmax + 3);
  b[13] = frand(0.05f, 3.5f) * sx * big;
  b[14] = frand(0.05f, 3.5f) * sy * big;
  b[15] = frand(0.05f, 3);
}

static void rand_frame(float *g /* 392 bytes */, int stop) {
  memset(g, 0, 392);
  ((int *)g)[10] = stop;
  rand_rot(g + 12);
  rand_rot(g + 21);
  for (int k = 0; k < 3; k++) { g[30 + k] = frand(-5, 5); g[33 + k] = frand(-3, 3); }
  g[39] = rnd() % 2 ? 1.0f : frand(0.5f, 2);
}
static void print_frame(const float *g) {
  printf(" %d", ((const int *)g)[10]);
  pw(g + 12, 9);
  pw(g + 21, 9);
  pw(g + 30, 3);
  pw(g + 33, 3);
  pw(g + 39, 1);
}

typedef struct { int first, n; } Call;
static Call g_calls[4096];
static int g_ncalls;
static int rec56(void *geom, int first, int n, int ctype, void *cprim, int blocal, void *gt, void *gto, void *buf, void *ids) {
  (void)geom; (void)ctype; (void)cprim; (void)blocal; (void)gt; (void)gto; (void)buf; (void)ids;
  if (g_ncalls < 4096) { g_calls[g_ncalls].first = first; g_calls[g_ncalls].n = n; }
  g_ncalls++;
  return n & 3;
}

static void region_cases(unsigned char *hf, int n) {
  float sx = *(float *)(hf + 660), sy = *(float *)(hf + 664);
  unsigned char *bv = hf + 728;
  void **hvt = *(void ***)hf;
  static void *vt2[64];
  memcpy(vt2, hvt, sizeof vt2);
  vt2[56] = (void *)rec56;
  for (int i = 0; i < n; i++) {
    FakeGeom other;
    other.vt = g_fake_vt;
    rand_box(other.box, sx, sy);
    float gt[98], go[98];
    int hasframe = rnd() % 2;
    if (hasframe) rand_frame(gt, rnd() % 2 ? 1 : 1);
    int hassweep = rnd() % 4 == 0;
    memset(go, 0, sizeof go);
    if (hassweep) {
      float d[3] = {frand(-1, 1), frand(-1, 1), frand(-1, 1)};
      float l = sqrtf(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
      for (int k = 0; k < 3; k++) go[62 + k] = d[k] / l;
      go[65] = rnd() % 3 ? 1.0f : -1.0f;
      go[66] = frand(-4, 4);
    }
    printf("reg");
    pw(other.box, 9);
    printf(" %d", ((int *)other.box)[9]);
    pw(other.box + 10, 6);
    printf(" %d", hasframe);
    if (hasframe) print_frame(gt);
    printf(" %d", hassweep);
    if (hassweep) { pw(go + 62, 3); pw(go + 65, 2); }
    int oldcap = *(int *)(hf + 796);
    int r = ((int (*)(void *, void *, void *, void *, char))VSLOT(hf, 53))(hf, hasframe ? gt : NULL, &other,
                                                                           hassweep ? go : NULL, 0);
    int cap = *(int *)(hf + 796), nt = *(int *)(hf + 120), nv = *(int *)(hf + 124);
    if (cap != oldcap) memset(*(void **)(hf + 776), 0, 4 * ((cap >> 5) + 1)); /* port zero-inits */
    printf(" | %d", r);
    if (hasframe) pw(gt + 30, 3);
    printf(" %d %d %d %d", *(int *)(bv + 24), *(int *)(bv + 28), *(int *)(bv + 32), *(int *)(bv + 36));
    pw((float *)(bv + 40), 2);
    printf(" %d %d %d %d %d", nt, nv, *(int *)(hf + 792), cap, *(int *)(hf + 168));
    printf("\n");
    if (r == 0 && i > 0 && rnd() % 2) continue;
    /* region mesh */
    printf("mesh |");
    pwi(*(void **)(hf + 80), 3 * nv);
    pwi(*(void **)(hf + 56), 3 * nt);
    pwi(*(void **)(hf + 96), 3 * nt);
    for (int k = 0; k < nt; k++) printf(" %d", (*(signed char **)(hf + 64))[k]);
    pwi(*(void **)(hf + 112), 3 * nt);
    pwi(*(void **)(hf + 776), nt > 0 ? ((nt - 1) >> 5) + 1 : 0);
    printf("\n");
    /* BV slots */
    float box[16];
    ((void (*)(void *, float *))VSLOT(bv, 2))(bv, box);
    printf("bv2 |");
    pwi(box, 16);
    printf(" %d", ((int (*)(void *))VSLOT(bv, 3))(bv));
    float cost = ((float (*)(void *, void *))VSLOT(bv, 4))(bv, hf);
    pw(&cost, 1);
    printf("\n");
    void *rec;
    ((void (*)(void *, void **, int, int))VSLOT(bv, 11))(bv, &rec, 0, 0);
    printf("bv11 |");
    pwi(rec, 27);
    printf(" %lld\n", (long long)((unsigned char *)rec - ARENA(0, 0x39DE8)));
    float R[9], off[3], sc;
    rand_rot(R);
    for (int k = 0; k < 3; k++) off[k] = frand(-20, 20);
    sc = rnd() % 2 ? 1.0f : frand(0.3f, 3);
    ((void (*)(void *, float *, float *, float, void **, int, int))VSLOT(bv, 9))(bv, R, off, sc, &rec, 0, 0);
    printf("bv9");
    pw(R, 9);
    pw(off, 3);
    pw(&sc, 1);
    printf(" |");
    pwi(rec, 27);
    printf("\n");
    /* slot 25 marks + slot 23 */
    for (int m = 0; m < 3; m++) {
      int nmark = nt > 0 ? irand(0, 6) : 0;
      printf("mark");
      for (int k = 0; k < nmark; k++) {
        int t = irand(0, nt - 1);
        ((void (*)(void *, int, void *))VSLOT(bv, 25))(bv, t, NULL);
        printf(" %d", t);
      }
      printf("\n");
      int crec[18];
      memset(crec, 0, sizeof crec);
      crec[0] = rnd() % 5 == 0 ? 1 : 0;
      float *cb = (float *)(crec + 2);
      rand_box(cb, sx, sy);
      if (rnd() % 2) { /* a leaf near the region */
        cb[10] = frand(-1, *(int *)(bv + 32) + 1) * sx;
        cb[11] = frand(-1, *(int *)(bv + 36) + 1) * sy;
        cb[13] = frand(0.1f, 2) * sx;
        cb[14] = frand(0.1f, 2) * sy;
      }
      int used = rnd() % 2, local = rnd() % 2;
      float fr[98];
      rand_frame(fr, 0);
      unsigned char gtz[400];
      memset(gtz, 0, sizeof gtz);
      memcpy(gtz, fr, 392);
      printf("s23 %d", crec[0]);
      pwi(cb, 16);
      printf(" %d %d", used, local);
      print_frame(fr);
      g_ncalls = 0;
      *(void ***)hf = vt2;
      int ret = ((int (*)(void *, int, void *, int, int, void *, void *))VSLOT(bv, 23))(bv, 0, crec, used, local, gtz, NULL);
      *(void ***)hf = hvt;
      printf(" | %d %d", ret, g_ncalls);
      for (int k = 0; k < g_ncalls && k < 4096; k++) printf(" %d:%d", g_calls[k].first, g_calls[k].n);
      printf("\n");
    }
  }
}

/* ---- rays ---- */
typedef struct {
  float off[3], R[9], s, v[3], w[3], com[3];
} Gwd; /* 88 bytes */

static void rand_gwd(Gwd *g, int ident) {
  memset(g, 0, sizeof *g);
  float id[9] = {1, 0, 0, 0, 1, 0, 0, 0, 1};
  if (ident) memcpy(g->R, id, 36); else rand_rot(g->R);
  g->s = ident || rnd() % 2 ? 1.0f : frand(0.5f, 2);
  for (int k = 0; k < 3; k++) {
    g->off[k] = ident ? 0 : frand(-30, 30);
    g->v[k] = rnd() % 3 ? frand(-3, 3) : 0;
  }
}
static void print_gwd(const Gwd *g) {
  pw(g->off, 3);
  pw(g->R, 9);
  pw(&g->s, 1);
  pw(g->v, 3);
}

static void ray_cases(unsigned char *hf, int n) {
  float sx = *(float *)(hf + 660), sy = *(float *)(hf + 664);
  int caller = 4; /* TLS slot empty -> the default 4 */
  for (int i = 0; i < n; i++) {
    Gwd g1, g2;
    rand_gwd(&g1, rnd() % 3 == 0);
    int hasg2 = rnd() % 3 != 0;
    rand_gwd(&g2, rnd() % 2);
    /* local ray, then into world through g1 (and back through g2 when present) */
    float lo[3], ld[3];
    int kind = irand(0, 6);
    lo[0] = frand(-2, g_nx + 2) * sx;
    lo[1] = frand(-2, g_ny + 2) * sy;
    lo[2] = frand(g_hmax + 0.1f, g_hmax + 6);
    float tx = frand(-2, g_nx + 2) * sx, ty = frand(-2, g_ny + 2) * sy, tz = frand(g_hmin - 4, g_hmin - 0.1f);
    switch (kind) {
    case 0: tx = lo[0]; ty = lo[1]; break; /* vertical */
    case 1: tz = lo[2] - frand(0, 0.5f); break; /* grazing / above */
    case 2: lo[2] = frand(g_hmin - 3, g_hmin - 0.2f); tz = frand(g_hmax + 0.1f, g_hmax + 3); break; /* from below */
    case 3: lo[2] = frand(g_hmin, g_hmax); break; /* starts near the surface */
    case 4: tz = frand(g_hmin, g_hmax); tx = lo[0] + frand(-1, 1) * sx; ty = lo[1] + frand(-1, 1) * sy; break;
    default: break;
    }
    ld[0] = tx - lo[0];
    ld[1] = ty - lo[1];
    ld[2] = tz - lo[2];
    if (rnd() % 20 == 0) { ld[0] *= 2000; ld[1] *= 2000; ld[2] *= 2000; } /* DrawRayOnGrid */
    float wo[3], wd[3];
    for (int r = 0; r < 3; r++) {
      wo[r] = (g1.R[3 * r] * lo[0] + g1.R[3 * r + 1] * lo[1] + g1.R[3 * r + 2] * lo[2]) * g1.s + g1.off[r];
      wd[r] = (g1.R[3 * r] * ld[0] + g1.R[3 * r + 1] * ld[1] + g1.R[3 * r + 2] * ld[2]) * g1.s;
    }
    float ro[3], rd[3];
    if (hasg2) { /* ray geometry such that g2 maps it to (wo, wd) */
      for (int c = 0; c < 3; c++) {
        float a = 0, b = 0;
        for (int r = 0; r < 3; r++) { a += g2.R[3 * r + c] * (wo[r] - g2.off[r]); b += g2.R[3 * r + c] * wd[r]; }
        ro[c] = a / g2.s;
        rd[c] = b / g2.s;
      }
    } else { memcpy(ro, wo, 12); memcpy(rd, wd, 12); }
    unsigned char *ray = (unsigned char *)calloc(1, 0x70);
    float rp[6] = {ro[0], ro[1], ro[2], rd[0], rd[1], rd[2]};
    ((void *(*)(void *, void *))VA(0x3514D950ULL))(ray, rp);
    unsigned char ip[256];
    memset(ip, 0, sizeof ip);
    int hasip = rnd() % 3 != 0, mode = 0, keep = 0, swap = rnd() % 4 == 0;
    float ctr[3], axis[3], ti = frand(0.01f, 2);
    for (int k = 0; k < 3; k++) { ctr[k] = frand(-10, 10); axis[k] = frand(-1, 1); }
    if (hasip) {
      mode = rnd() % 2;
      if (mode == 1 && rnd() % 2) axis[0] = axis[1] = axis[2] = 0;
      keep = rnd() % 4 == 0;
      *(int *)ip = mode;
      memcpy(ip + 4, ctr, 12);
      memcpy(ip + 16, axis, 12);
      memcpy(ip + 28, &ti, 4);
      ip[97] = (unsigned char)keep;
    }
    if (hasip && mode == 0 && !hasg2) swap = 0; /* the unprojection path reads gwd2->v unchecked */
    int before = irand(0, 5);
    *(int *)ARENA(caller, 0xE9B8) = before;
    *(int *)(hf + 20) = swap ? 0 : 1;
    printf("ray");
    print_gwd(&g1);
    printf(" %d", hasg2);
    print_gwd(&g2);
    pw(rp, 6);
    printf(" %d %d", hasip, mode);
    pw(ctr, 3);
    pw(axis, 3);
    pw(&ti, 1);
    printf(" %d %d %d", keep, swap, before);
    unsigned char *pc = NULL;
    int r = ((int (*)(void *, void *, void *, void *, void *, void **))VSLOT(hf, 10))(hf, ray, &g1, hasg2 ? &g2 : NULL,
                                                                                 hasip ? ip : NULL, (void **)&pc);
    printf(" | %d %d", r, *(int *)ARENA(caller, 0xE9B8));
    if (r) {
      long long slot = (long long)(pc - ARENA(caller, 0xC7B8)) / 136;
      printf(" %lld", slot);
      pwi(pc, 2);
      pwi(pc + 8, 9);
      printf(" %d", *(int *)(pc + 44));
      pwi(pc + 48, 1);
      printf(" %d %d %d %d", *(int *)(pc + 52), *(int *)(pc + 56), *(int *)(pc + 68), *(int *)(pc + 72));
      pwi(pc + 112, 3);
      printf(" %d %d", *(int *)(pc + 104), *(void **)(pc + 88) == pc + 112);
      if (hasip) printf(" %d", *(unsigned char **)(ip + 128) == ARENA(caller, 0xC7B8));
    }
    printf("\n");
    *(int *)(hf + 20) = 1;
    free(ray);
  }
}

/* ---- the generic CGeometry::Intersect (0x3514FD70) with a heightfield and a ray ---- */
static void contact1(unsigned char *c) { /* tests/p1c_isect_probe.c's print_contact on one line */
  printf(" ; c %016llx", (unsigned long long)*(uint64_t *)c);
  for (int i = 8; i < 44; i += 4) printf(" %08x", *(uint32_t *)(c + i));
  printf(" %d %08x %d %d %d %d %d %d %d %d", *(int *)(c + 44), *(uint32_t *)(c + 48), *(int *)(c + 52),
         *(int *)(c + 56), *(int *)(c + 60), *(int *)(c + 64), *(int *)(c + 68), *(int *)(c + 72),
         *(int *)(c + 76), *(int *)(c + 80));
  int nb = *(int *)(c + 104);
  float *bp = *(float **)(c + 88);
  const char *kind = (unsigned char *)bp == c + 8 ? "pt" : (unsigned char *)bp == c + 112 ? "center" : "pool";
  printf(" center %08x %08x %08x cons %d nb %d %s", *(uint32_t *)(c + 112), *(uint32_t *)(c + 116),
         *(uint32_t *)(c + 120), c[124], nb, kind);
  if (nb > 1 || bp != (float *)(c + 8)) {
    printf(" ; b");
    for (int i = 0; i < 3 * nb; i++) printf(" %08x", fb(bp[i]));
    int *ib = *(int **)(c + 96);
    if (ib) {
      printf(" ; bi");
      for (int i = 0; i < 2 * nb; i++) printf(" %d", ib[i]);
    }
  }
  unsigned char *a = *(unsigned char **)(c + 128);
  if (a) {
    int npt = *(int *)(a + 4);
    printf(" ; a %d %d %d %08x n %08x %08x %08x", *(int *)a, npt, *(int *)(a + 8), *(uint32_t *)(a + 12),
           *(uint32_t *)(a + 56), *(uint32_t *)(a + 60), *(uint32_t *)(a + 64));
    float *pt = *(float **)(a + 48);
    int *p0 = *(int **)(a + 16), *p1 = *(int **)(a + 24), *f0 = *(int **)(a + 32), *f1 = *(int **)(a + 40);
    for (int i = 0; i < npt; i++)
      printf(" ; ap %08x %08x %08x %d %d %d %d", fb(pt[3 * i]), fb(pt[3 * i + 1]), fb(pt[3 * i + 2]), p0[i], f0[i],
             p1[i], f1[i]);
  }
}

static void isect_cases(unsigned char *hf, int n) {
  float sx = *(float *)(hf + 660), sy = *(float *)(hf + 664);
  unsigned char *ar = ARENA(4, 0);
  for (int i = 0; i < n; i++) {
    float gh[24], gr[24];
    memset(gh, 0, sizeof gh);
    memset(gr, 0, sizeof gr);
    rand_gwd((Gwd *)gh, rnd() % 3 == 0);
    rand_gwd((Gwd *)gr, rnd() % 2);
    Gwd *g1 = (Gwd *)gh;
    float lo[3], ld[3];
    lo[0] = frand(-1, g_nx + 1) * sx;
    lo[1] = frand(-1, g_ny + 1) * sy;
    lo[2] = frand(g_hmax + 0.1f, g_hmax + 3);
    float tx = lo[0] + frand(-2, 2) * sx, ty = lo[1] + frand(-2, 2) * sy;
    float tz = rnd() % 3 ? frand(g_hmin - 3, g_hmin - 0.1f) : frand(g_hmin, g_hmax);
    if (rnd() % 4 == 0) { tx = lo[0]; ty = lo[1]; }
    ld[0] = tx - lo[0];
    ld[1] = ty - lo[1];
    ld[2] = tz - lo[2];
    float wo[3], wd[3], ro[3], rd[3];
    for (int r = 0; r < 3; r++) {
      wo[r] = (g1->R[3 * r] * lo[0] + g1->R[3 * r + 1] * lo[1] + g1->R[3 * r + 2] * lo[2]) * g1->s + g1->off[r];
      wd[r] = (g1->R[3 * r] * ld[0] + g1->R[3 * r + 1] * ld[1] + g1->R[3 * r + 2] * ld[2]) * g1->s;
    }
    Gwd *g2 = (Gwd *)gr;
    for (int c = 0; c < 3; c++) {
      float a = 0, b = 0;
      for (int r = 0; r < 3; r++) { a += g2->R[3 * r + c] * (wo[r] - g2->off[r]); b += g2->R[3 * r + c] * wd[r]; }
      ro[c] = a / g2->s;
      rd[c] = b / g2->s;
    }
    unsigned char *ray = (unsigned char *)calloc(1, 0x70);
    float rp[6] = {ro[0], ro[1], ro[2], rd[0], rd[1], rd[2]};
    ((void *(*)(void *, void *))VA(0x3514D950ULL))(ray, rp);
    int order = rnd() % 2, hasip = rnd() % 2;
    uint32_t ip[64];
    memset(ip, 0, sizeof ip);
    float ti = frand(0.01f, 2), vmin = rnd() % 2 ? 1e10f : frand(0, 2);
    int noarea = rnd() % 2;
    if (hasip) {
      float *f = (float *)ip;
      f[7] = ti;
      f[8] = vmin;
      f[9] = 0.0174533f;
      f[14] = 0; f[15] = 0; f[16] = 1;
      f[17] = 1e10f;
      for (int k = 18; k < 24; k++) f[k] = 1e11f;
      ((unsigned char *)ip)[99] = (unsigned char)noarea;
    }
    printf("isect %d", order);
    pwi(gh, 22);
    pwi(gr, 22);
    pw(rp, 6);
    printf(" %d", hasip);
    pw(&ti, 1);
    pw(&vmin, 1);
    printf(" %d", noarea);
    memset(ar + 0xC7B8, 0, 64 * 136);
    memset(ar + 0xE9C0, 0, 32 * 72);
    unsigned char *contacts = NULL;
    int oldcap = *(int *)(hf + 796);
    typedef int (*fn_isect)(void *, void *, void *, void *, void *, unsigned char **);
    int r = order ? ((fn_isect)VA(0x3514FD70ULL))(ray, hf, gr, gh, hasip ? ip : NULL, &contacts)
                  : ((fn_isect)VA(0x3514FD70ULL))(hf, ray, gh, gr, hasip ? ip : NULL, &contacts);
    int cap = *(int *)(hf + 796);
    if (cap != oldcap) memset(*(void **)(hf + 776), 0, 4 * ((cap >> 5) + 1));
    printf(" | %d", r);
    for (int k = 0; k < r; k++) contact1(contacts + 136 * k);
    printf("\n");
    free(ray);
  }
}

int main(int argc, char **argv) {
  p1c_init(&argc, &argv);
  if (argc < 4 || strcmp(argv[1], "run")) { printf("usage: run seed ngrids\n"); return 1; }
  g_rng = 0x9E3779B97F4A7C15ULL ^ (uint64_t)atoi(argv[2]) * 0x2545F4914F6CDD1DULL;
  int ng = atoi(argv[3]);
  int isect = argc > 4 && !strcmp(argv[4], "isect");
  g_fake_vt[6] = (void *)fake_bbox;
  /* the per-caller hf records start as the static initialiser leaves them (type 2) */
  for (int g = 0; g < ng; g++) {
    unsigned char *hf = (unsigned char *)calloc(1, 0x320);
    HfPrim src;
    make_grid(hf, &src);
    dump_create(hf, &src);
    pis_cases(hf, 150);
    region_cases(hf, 30);
    ray_cases(hf, 80);
    if (isect) isect_cases(hf, 60);
    printf("end\n");
  }
  return 0;
}
