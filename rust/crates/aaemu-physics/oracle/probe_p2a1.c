/* P2a-1 probes: the box pair functions of the INTERSECT / OVERLAP tables, called directly inside
 * cryphysics_dedicate.dll on generated primitives (`physoracle --math p2a1_<pair> COUNT SEED`).
 * Included by physoracle.c (additive; owned by the P2a-1 box-pairs package).
 *
 * Output, one line per case (all floats / ints as raw 32-bit hex words):
 *   INTERSECT: in <A words> <B words> out <ret> <prim_inters words> b <n> <border words>
 *   OVERLAP:   in <A words> <B words> out <ret>
 * prim_inters words are +0..+52 (14 words), +64, +68, +72..+84 (6 words); every word starts as
 * P2A1_SENT so untouched fields are visible. The border buffer (+56) holds 64 points; `b` is
 * printed only when +64 was written with a count in [0, 64]. Heightfield cases use the
 * deterministic callbacks p2a1_hf_height / p2a1_hf_surf (the Rust test has the same formulas).
 */
#include <math.h>
#define P2A1_SENT 0x7FC0DEADu
typedef int(__fastcall *p2a1_fn3)(void *, void *, void *);
typedef int(__fastcall *p2a1_fn2)(void *, void *, void *);

static float p2a1_hf_height(int ix, int iy) { return (float)((ix * 7 + iy * 13) & 15) * 0.25f - 1.0f; }
static unsigned short p2a1_hf_surf(int ix, int iy) { return (unsigned short)(((ix + 3 * iy) % 11 == 0) ? 0x8000 : 0); }

static float p2a1_pick(const float *v, int n) { return v[splitmix() % (uint64_t)n]; }
static void p2a1_norm(float *v) {
  float l = sqrtf(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
  if (l > 0) { v[0] /= l; v[1] /= l; v[2] /= l; } else { v[0] = 0; v[1] = 0; v[2] = 1; }
}
/* a basis of the given style: 0 identity, 1 random, 2 axis permutation with signs, 3 about z,
   4 tiny perturbation of identity (oriented) */
static int p2a1_basis(float *m, int style) {
  float q[4];
  int i;
  for (i = 0; i < 9; i++) m[i] = (i % 4 == 0) ? 1.0f : 0.0f;
  if (style == 0) return 0;
  if (style == 1) rand_quat(q);
  else if (style == 3) { float a = frand(-3.2f, 3.2f); q[0] = 0; q[1] = 0; q[2] = sinf(a * 0.5f); q[3] = cosf(a * 0.5f); }
  else if (style == 4) { q[0] = frand(-0.01f, 0.01f); q[1] = frand(-0.01f, 0.01f); q[2] = frand(-0.01f, 0.01f); q[3] = 1; }
  else {
    int p = (int)(splitmix() % 6), perm[6][3] = {{0,1,2},{0,2,1},{1,0,2},{1,2,0},{2,0,1},{2,1,0}};
    for (i = 0; i < 9; i++) m[i] = 0;
    for (i = 0; i < 3; i++) m[i * 3 + perm[p][i]] = (splitmix() & 1) ? 1.0f : -1.0f;
    return 1;
  }
  if (style == 4) { float l = sqrtf(q[0]*q[0]+q[1]*q[1]+q[2]*q[2]+q[3]*q[3]); for (i = 0; i < 4; i++) q[i] /= l; }
  ((fn_q2m)VA(g_phys, 0x350247B0ULL))(m, q);
  return 1;
}
static const float p2a1_q[] = {0.25f, 0.5f, 0.75f, 1.0f, 1.5f, 2.0f};
/* box: basis[9] oriented center[3] size[3] (16 words) */
static void p2a1_gen_box(float *b, int i) {
  int style = (int)(splitmix() % 5), k;
  int quant = (i % 7) == 5;
  if (quant) style = (splitmix() & 1) ? 0 : 2;
  int o = p2a1_basis(b, style);
  memcpy(b + 9, &o, 4);
  for (k = 0; k < 3; k++) b[10 + k] = quant ? (float)((int)(splitmix() % 9) - 4) * 0.25f : frand(-3, 3);
  for (k = 0; k < 3; k++) b[13 + k] = quant ? p2a1_pick(p2a1_q, 6) : frand(0.05f, 2.0f);
  if (i % 11 == 4) b[14] = b[15] = b[13];
  if (i % 23 == 17) b[13 + (int)(splitmix() % 3)] = 0.0f; /* flat box */
}
/* a point near the box (offset scaled by `reach`); quantised cases snap to quarters */
static void p2a1_near(const float *box, float reach, float *p, int i) {
  int k;
  if ((i % 7) == 5) {
    for (k = 0; k < 3; k++) p[k] = box[10 + k] + (float)((int)(splitmix() % 17) - 8) * 0.25f;
    return;
  }
  float d[3];
  for (k = 0; k < 3; k++) d[k] = frand(-1, 1);
  float s = frand(0, 1.25f) * reach;
  if (i % 13 == 6) s = 0;
  for (k = 0; k < 3; k++) p[k] = box[10 + k] + d[k] * s;
}
static float p2a1_reach(const float *box, float extra) { return box[13] + box[14] + box[15] + extra; }
/* sphere: center[3] r */
static void p2a1_gen_sph(const float *box, float *s, int i) {
  s[3] = (i % 7 == 5) ? p2a1_pick(p2a1_q, 6) : frand(0.02f, 2.0f);
  p2a1_near(box, p2a1_reach(box, s[3]), s, i);
  if (i % 19 == 3) s[3] = 0.0f;
}
/* ray: origin[3] dir[3] */
static void p2a1_gen_ray(const float *box, float *r, int i) {
  int k, m = (int)(splitmix() % 6);
  p2a1_near(box, p2a1_reach(box, 1.0f) * 1.5f, r, i);
  for (k = 0; k < 3; k++) r[3 + k] = frand(-6, 6);
  if (m == 1) { int a = (int)(splitmix() % 3); r[3] = r[4] = r[5] = 0; r[3 + a] = frand(-6, 6); }
  else if (m == 2) { int a = (int)(splitmix() % 3); r[3 + a] = 0; }
  else if (m == 3) { for (k = 0; k < 3; k++) r[3 + k] = (box[10 + k] - r[k]) * frand(0.5f, 3); }
  else if (m == 4) { for (k = 0; k < 3; k++) r[3 + k] = box[3 * (int)(splitmix() % 3) + k] * frand(-6, 6); }
  if (i % 29 == 7) r[3] = r[4] = r[5] = 0;
}
/* cylinder / capsule: center[3] axis[3] r hh */
static void p2a1_gen_cyl(const float *box, float *c, int i) {
  int k, m = (int)(splitmix() % 6);
  c[6] = (i % 7 == 5) ? p2a1_pick(p2a1_q, 6) : frand(0.05f, 1.5f);
  c[7] = (i % 7 == 5) ? p2a1_pick(p2a1_q, 6) : frand(0.0f, 2.0f);
  if (i % 31 == 3) c[7] = 0;
  p2a1_near(box, p2a1_reach(box, c[6] + c[7]), c, i);
  for (k = 0; k < 3; k++) c[3 + k] = frand(-1, 1);
  if (m == 1 || m == 2) { int a = (int)(splitmix() % 3); float sg = (splitmix() & 1) ? 1.0f : -1.0f; for (k = 0; k < 3; k++) c[3 + k] = box[3 * a + k] * sg; }
  else if (m == 3) { int a = (int)(splitmix() % 3); c[3] = c[4] = c[5] = 0; c[3 + a] = (splitmix() & 1) ? 1.0f : -1.0f; }
  else if (m == 4) { int a = (int)(splitmix() % 3); for (k = 0; k < 3; k++) c[3 + k] = box[3 * a + k] + frand(-0.002f, 0.002f); }
  if (m != 1 && m != 2 && m != 3) p2a1_norm(c + 3);
}
/* triangle: pt[3][3] n[3] */
static void p2a1_gen_tri(const float *box, float *t, int i) {
  int k;
  float reach = p2a1_reach(box, 0.5f);
  float c[3];
  p2a1_near(box, reach, c, i);
  for (k = 0; k < 9; k++) t[k] = c[k % 3] + frand(-1, 1) * reach * 0.8f;
  if (i % 9 == 2) for (k = 0; k < 3; k++) t[3 * k + 2] = c[2]; /* horizontal */
  float e1[3], e2[3];
  for (k = 0; k < 3; k++) { e1[k] = t[3 + k] - t[k]; e2[k] = t[6 + k] - t[k]; }
  t[9] = e1[1] * e2[2] - e1[2] * e2[1];
  t[10] = e1[2] * e2[0] - e1[0] * e2[2];
  t[11] = e1[0] * e2[1] - e1[1] * e2[0];
  p2a1_norm(t + 9);
}
/* heightfield prim (120 bytes = 30 words), cells around the box */
static void p2a1_gen_hf(const float *box, uint32_t *w, int i) {
  float *f = (float *)w;
  int k;
  memset(w, 0, 120);
  int o = (i % 4 == 3) ? p2a1_basis(f, 3) : p2a1_basis(f, 0);
  memcpy(w + 9, &o, 4);
  f[10] = box[10] - frand(2, 8); f[11] = box[11] - frand(2, 8); f[12] = (i % 5 == 1) ? frand(-1, 1) : 0.0f;
  f[13] = p2a1_pick(p2a1_q, 6); f[14] = p2a1_pick(p2a1_q, 6);
  f[15] = 1.0f / f[13]; f[16] = 1.0f / f[14];
  w[17] = 32 + (uint32_t)(splitmix() % 32); w[18] = 32 + (uint32_t)(splitmix() % 32);
  w[19] = w[17]; w[20] = 1;
  f[21] = (i % 3 == 0) ? 1.0f : frand(0.5f, 2);
  w[22] = 0x8000u | (0xFFFFu << 16); /* typemask 0x8000, heightmask 0xFFFF */
  w[23] = 1; w[24] = 15; w[25] = 0;
  (void)k;
}

static void p2a1_print_out(int ret, const uint32_t *o, const float *border) {
  int k;
  printf(" out %08x", (uint32_t)ret);
  for (k = 0; k < 14; k++) printf(" %08x", o[k]);
  printf(" %08x %08x", o[16], o[17]);
  for (k = 18; k < 22; k++) printf(" %08x", o[k]);
  if (o[16] <= 64) {
    printf(" b %u", o[16]);
    for (k = 0; k < (int)o[16] * 3; k++) printf(" %08x", fbits(border[k]));
  }
  printf("\n");
}

static void p2a1_probe(const char *name, int count) {
  static const struct { const char *n; uint64_t va; int kind; int swap; } T[] = {
    /* kind: 0 box-box 1 box-ray 2 box-sph 3 box-cyl 4 box-cap ; 10+ overlap: 10 bb 11 btri 12 bhf 13 bray 14 bsph */
    {"p2a1_box_box", 0x3516AE00ULL, 0, 0}, {"p2a1_box_ray", 0x3515F110ULL, 1, 0},
    {"p2a1_box_sph", 0x3515D460ULL, 2, 0}, {"p2a1_box_cyl", 0x3515BA70ULL, 3, 0},
    {"p2a1_box_cap", 0x3515D8A0ULL, 4, 0}, {"p2a1_ray_box", 0x3515F690ULL, 1, 1},
    {"p2a1_sph_box", 0x3515D820ULL, 2, 1}, {"p2a1_cyl_box", 0x3515D3F0ULL, 3, 1},
    {"p2a1_cap_box", 0x3515F0A0ULL, 4, 1},
    {"p2a1_ov_box_box", 0x351981B0ULL, 10, 0}, {"p2a1_ov_box_tri", 0x351968F0ULL, 11, 0},
    {"p2a1_ov_box_hf", 0x351986E0ULL, 12, 0}, {"p2a1_ov_box_ray", 0x35195EA0ULL, 13, 0},
    {"p2a1_ov_box_sph", 0x35196CE0ULL, 14, 0}, {"p2a1_ov_ray_box", 0x35196010ULL, 13, 1},
    {"p2a1_ov_sph_box", 0x35196E50ULL, 14, 1}, {"p2a1_ov_tri_box", 0x35196CC0ULL, 11, 1},
    {"p2a1_ov_hf_box", 0x35199120ULL, 12, 1},
  };
  int t = -1, i, k;
  for (i = 0; i < (int)(sizeof T / sizeof T[0]); i++) if (!strcmp(T[i].n, name)) t = i;
  if (t < 0) die("unknown p2a1 probe", name);
  void *fn = VA(g_phys, T[t].va);
  int kind = T[t].kind;
  for (i = 0; i < count; i++) {
    __declspec(align(16)) float a[32], b[48];
    __declspec(align(16)) uint32_t out[32];
    static __declspec(align(16)) float border[64 * 3 + 16];
    int na = 16, nb = 0, ret;
    memset(a, 0, sizeof a);
    memset(b, 0, sizeof b);
    p2a1_gen_box(a, i);
    if (kind == 0 || kind == 10) {
      p2a1_gen_box(b, i / 3 + 1000);
      float p[3];
      p2a1_near(a, p2a1_reach(a, b[13] + b[14] + b[15]) * 0.8f, p, i);
      memcpy(b + 10, p, 12);
      if (i % 41 == 9) memcpy(b, a, sizeof(float) * 16); /* identical boxes */
      if (i % 43 == 11) { /* face contact along one axis of an aligned pair */
        int ax = (int)(splitmix() % 3);
        memcpy(b, a, sizeof(float) * 10);
        for (k = 0; k < 3; k++) b[10 + k] = a[10 + k];
        b[10 + ax] += a[13 + ax] + b[13 + ax];
      }
      nb = 16;
    } else if (kind == 1 || kind == 13) { p2a1_gen_ray(a, b, i); nb = 6; }
    else if (kind == 2 || kind == 14) { p2a1_gen_sph(a, b, i); nb = 4; }
    else if (kind == 3 || kind == 4) { p2a1_gen_cyl(a, b, i); nb = 8; }
    else if (kind == 11) { p2a1_gen_tri(a, b, i); nb = 12; }
    else if (kind == 12) { p2a1_gen_hf(a, (uint32_t *)b, i); nb = 26; }
    printf("in");
    out_f(a, na);
    out_f(b, nb);
    if (kind == 12) {
      void *cb[2] = {(void *)p2a1_hf_height, (void *)p2a1_hf_surf};
      memcpy((char *)b + 104, cb, 16);
    }
    void *pa = a, *pb = b;
    if (T[t].swap) { pa = b; pb = a; }
    if (kind < 10) {
      for (k = 0; k < 32; k++) out[k] = P2A1_SENT;
      for (k = 0; k < 64 * 3 + 16; k++) border[k] = bitsf(P2A1_SENT);
      void *bp = border;
      memcpy(out + 14, &bp, 8);
      ret = ((p2a1_fn3)fn)(pa, pb, out);
      p2a1_print_out(ret, out, border);
    } else {
      uint32_t scratch[64];
      memset(scratch, 0, sizeof scratch);
      ret = ((p2a1_fn2)fn)(pa, pb, scratch) & 0xFF;
      printf(" out %08x\n", (uint32_t)ret);
    }
  }
}
