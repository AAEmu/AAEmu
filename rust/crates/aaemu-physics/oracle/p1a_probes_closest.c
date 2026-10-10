/* P1a probes for src/geometry/shapes/closest.rs, #included by p1a_probes.c (after its helpers).
 *
 *   cprobe type=box|sphere|cylinder|capsule n=COUNT seed=SEED dist=D [mode=1]
 *
 * Calls slot 11 FindClosestPoint (box 0x3513D020, sphere 0x351AF660, cylinder 0x35145CC0,
 * capsule 0x3513FCA0) on segments drawn from distribution D and prints each case in the
 * `gprobe probe=closest` format:
 *
 *   gp closest <type> P <prim> I <gwd 13> <ptdst0 3> <ptdst1 3> <iPrim> <iFeature> <nMaxIters>
 *      O <ret> <iPrim> <iFeature> <ptres 6>
 *
 * Distributions (end points are built in the primitive's local frame, then placed by gwd):
 *   0  around the primitive (both ends random in a box 2.5x the bounding radius)
 *   1  one or both ends inside the primitive
 *   2  near-degenerate segments (lengths 0 .. 1e-2, straddling the size-relative thresholds)
 *   3  near-parallel to a box axis / the cylinder axis, outside the primitive
 *   4  long segments crossing the primitive's neighbourhood
 *   5  end points exactly on faces / edges / vertices / cap rims (local coordinates on the
 *      surface), segments through them
 *   6  extreme placements: scales 0.01..100, large offsets
 *   7  box: segments in the (+,+,+) octant beyond the box (the vertex branch); others: as 3
 *   8  segments crossing a cap plane near the rim (cylinder/capsule), near an edge (box)
 * mode=1: identity rotation and scale 1 (as gprobe).
 *
 *   crtd fn=exp|log|sin|cos|atan2 (x=HEX16 [y=HEX16] | n=N seed=S lo=A hi=B)
 *
 * MSVCR100's double functions (used by the cylinder's polynomial root finder) on exact inputs
 * or on full-precision random doubles in [lo, hi) (53 random bits; atan2 draws y and x
 * independently). Prints `crtd <fn> <x> [<y>] <result>` as raw 64-bit words.
 */

#include <math.h>

static uint64_t splitmix(void);

/* orthonormal frame of a unit axis (only to aim the inputs) */
static void cp_frame(const float *a, float *e0, float *e1) {
  float t[3] = {0, 0, 0};
  t[fabsf(a[0]) < 0.6f ? 0 : (fabsf(a[1]) < 0.6f ? 1 : 2)] = 1;
  e0[0] = a[1] * t[2] - a[2] * t[1];
  e0[1] = a[2] * t[0] - a[0] * t[2];
  e0[2] = a[0] * t[1] - a[1] * t[0];
  float n = sqrtf(e0[0] * e0[0] + e0[1] * e0[1] + e0[2] * e0[2]);
  for (int i = 0; i < 3; i++) e0[i] /= n;
  e1[0] = a[1] * e0[2] - a[2] * e0[1];
  e1[1] = a[2] * e0[0] - a[0] * e0[2];
  e1[2] = a[0] * e0[1] - a[1] * e0[0];
}

/* local (primitive frame) point -> world: box: c + Basis^T l; cylinder/capsule: c + e0 lx +
   e1 ly + axis lz; sphere: c + l. Then R (x * scale) + offset. */
static void cp_world(int type, const float *p, const float *gwd, const float *l, float *w) {
  float x[3];
  if (type == 0) {
    for (int k = 0; k < 3; k++) x[k] = p[10 + k] + p[k] * l[0] + p[3 + k] * l[1] + p[6 + k] * l[2];
  } else if (type == 5 || type == 6) {
    float e0[3], e1[3];
    cp_frame(p + 3, e0, e1);
    for (int k = 0; k < 3; k++) x[k] = p[k] + e0[k] * l[0] + e1[k] * l[1] + p[3 + k] * l[2];
  } else {
    for (int k = 0; k < 3; k++) x[k] = p[k] + l[k];
  }
  for (int i = 0; i < 3; i++)
    w[i] = gwd[i] + gwd[12] * (gwd[3 + 3 * i] * x[0] + gwd[4 + 3 * i] * x[1] + gwd[5 + 3 * i] * x[2]);
}

/* local half extents of the primitive (box size; sphere r; cylinder r, r, hh; capsule r, r, hh+r) */
static void cp_ext(int type, const float *p, float *e) {
  if (type == 0) memcpy(e, p + 13, 12);
  else if (type == 4) e[0] = e[1] = e[2] = p[3];
  else { e[0] = e[1] = p[6]; e[2] = p[7] + (type == 6 ? p[6] : 0); }
}

/* a random local point on the surface feature of the primitive */
static void cp_on_feature(int type, const float *p, float *l) {
  float e[3];
  cp_ext(type, p, e);
  if (type == 0) {
    int f = (int)frand(0, 2.999f); /* 0 vertex, 1 edge, 2 face */
    for (int k = 0; k < 3; k++) l[k] = (frand(0, 1) < 0.5f ? -1 : 1) * e[k];
    if (f >= 1) { int a = (int)frand(0, 2.999f); l[a] = frand(-1, 1) * e[a]; }
    if (f == 2) { int a = (int)frand(0, 2.999f); l[a] = frand(-1, 1) * e[a]; }
  } else if (type == 4) {
    float u[3];
    p1a_rand_unit(u);
    for (int k = 0; k < 3; k++) l[k] = u[k] * e[0];
  } else {
    float ang = frand(0, 6.2831853f), r = p[6], hh = p[7];
    int f = (int)frand(0, 2.999f); /* 0 rim, 1 side, 2 cap */
    float rr = f == 2 ? frand(0, 1) * r : r;
    l[0] = cosf(ang) * rr;
    l[1] = sinf(ang) * rr;
    l[2] = f == 1 ? frand(-1, 1) * hh : (frand(0, 1) < 0.5f ? -hh : hh);
    if (type == 6 && f != 1) { /* capsule cap: on the hemisphere */
      float u[3];
      p1a_rand_unit(u);
      if ((u[2] < 0) != (l[2] < 0)) u[2] = -u[2];
      l[0] = u[0] * r; l[1] = u[1] * r; l[2] = l[2] + u[2] * r;
    }
  }
}

static void cp_case(int type, int dist, int mode, int i) {
  __declspec(align(16)) float prim[32], gwd[32], in[16], out[16];
  float rad = p1a_rand_prim(type, prim);
  int pw = p1a_prim_words(type);
  void *g = p1a_create(type, prim);
  float e[3], l0[3], l1[3], dir[3];
  int ip, ifeat;
  p1a_rand_gwd(gwd, mode);
  if (dist == 6) {
    float r = frand(0, 1);
    gwd[12] = r < 0.3f ? frand(0.01f, 0.1f) : (r < 0.6f ? frand(10, 100) : frand(0.5f, 2));
    if (frand(0, 1) < 0.5f) for (int k = 0; k < 3; k++) gwd[k] = frand(-3000, 3000);
  }
  cp_ext(type, prim, e);
  float big = rad + 0.2f;
  switch (dist) {
  default: /* 0 */
    for (int k = 0; k < 3; k++) l0[k] = frand(-2.5f, 2.5f) * big;
    for (int k = 0; k < 3; k++) l1[k] = frand(-2.5f, 2.5f) * big;
    break;
  case 1:
    for (int k = 0; k < 3; k++) l0[k] = frand(-0.95f, 0.95f) * e[k] * (type == 0 ? 1.0f : 0.7f);
    if (frand(0, 1) < 0.5f) for (int k = 0; k < 3; k++) l1[k] = frand(-0.95f, 0.95f) * e[k] * (type == 0 ? 1.0f : 0.7f);
    else for (int k = 0; k < 3; k++) l1[k] = frand(-2.5f, 2.5f) * big;
    break;
  case 2: {
    for (int k = 0; k < 3; k++) l0[k] = frand(-2, 2) * big;
    if (frand(0, 1) < 0.3f) cp_on_feature(type, prim, l0);
    float len = frand(0, 1);
    len = len < 0.2f ? 0.0f : powf(10.0f, frand(-6, -2)) * (frand(0, 1) < 0.5f ? 1 : e[0]);
    p1a_rand_unit(dir);
    for (int k = 0; k < 3; k++) l1[k] = l0[k] + dir[k] * len;
    break;
  }
  case 3: case 7: case 8: {
    int ax = type == 0 ? (int)frand(0, 2.999f) : 2;
    if (dist == 7 && type == 0) { /* beyond the (+,+,+) corner */
      for (int k = 0; k < 3; k++) l0[k] = e[k] * frand(1.0f, 3.0f) + frand(0, 1);
      for (int k = 0; k < 3; k++) l1[k] = e[k] * frand(1.0f, 3.0f) + frand(0, 1);
      if (frand(0, 1) < 0.5f) { int a = (int)frand(0, 2.999f); l1[a] = -l1[a]; }
      break;
    }
    if (dist == 8) { /* crossing near an edge / the rim */
      float c[3];
      cp_on_feature(type, prim, c);
      if (type != 0) { float ang = frand(0, 6.2831853f); c[0] = cosf(ang) * prim[6]; c[1] = sinf(ang) * prim[6]; c[2] = (frand(0, 1) < 0.5f ? -1 : 1) * prim[7]; }
      p1a_rand_unit(dir);
      float off = frand(-0.3f, 0.3f) * big, len = frand(0.05f, 2) * big;
      for (int k = 0; k < 3; k++) { l0[k] = c[k] + dir[k] * (off - len); l1[k] = c[k] + dir[k] * (off + len); }
      if (frand(0, 1) < 0.3f) for (int k = 0; k < 3; k++) l1[k] = l0[k] + (l1[k] - l0[k]) * frand(0.5f, 1);
      break;
    }
    /* near-parallel to axis `ax`, offset outside the primitive */
    for (int k = 0; k < 3; k++) l0[k] = (frand(0, 1) < 0.5f ? -1 : 1) * e[k] * frand(1.0f, 1.8f);
    if (frand(0, 1) < 0.3f) l0[(ax + 1) % 3] = frand(-1, 1) * e[(ax + 1) % 3];
    float len = frand(0.1f, 3) * big, tilt = frand(0, 1) < 0.3f ? 0.0f : powf(10.0f, frand(-6, -1));
    for (int k = 0; k < 3; k++) l1[k] = l0[k] + (k == ax ? len : frand(-1, 1) * tilt * len);
    l0[ax] -= frand(0, 1) * len;
    break;
  }
  case 4: {
    float c[3];
    for (int k = 0; k < 3; k++) c[k] = frand(-1.5f, 1.5f) * big;
    p1a_rand_unit(dir);
    float len = frand(2, 20) * big;
    for (int k = 0; k < 3; k++) { l0[k] = c[k] - dir[k] * len * frand(0.2f, 1); l1[k] = c[k] + dir[k] * len * frand(0.2f, 1); }
    break;
  }
  case 5: {
    cp_on_feature(type, prim, l0);
    float r = frand(0, 1);
    if (r < 0.3f) cp_on_feature(type, prim, l1);
    else if (r < 0.5f) memcpy(l1, l0, 12);
    else {
      p1a_rand_unit(dir);
      float len = frand(0.01f, 2) * big;
      for (int k = 0; k < 3; k++) l1[k] = l0[k] + dir[k] * len;
      if (r < 0.75f) for (int k = 0; k < 3; k++) l0[k] -= dir[k] * len * frand(0, 1);
    }
    break;
  }
  case 6:
    for (int k = 0; k < 3; k++) l0[k] = frand(-2.5f, 2.5f) * big;
    if (frand(0, 1) < 0.25f) memcpy(l1, l0, 12);
    else for (int k = 0; k < 3; k++) l1[k] = frand(-2.5f, 2.5f) * big;
    break;
  }
  if (frand(0, 1) < 0.5f) { float t[3]; memcpy(t, l0, 12); memcpy(l0, l1, 12); memcpy(l1, t, 12); }
  cp_world(type, prim, gwd, l0, in);
  cp_world(type, prim, gwd, l1, in + 3);
  ip = (int)frand(-1, 2.99f);
  ifeat = (i % 2) ? -1 : (int)frand(0, 100);
  int niter = (int)frand(1, 20.99f);
  memset(out, 0, 64);
  printf("gp closest %d", type);
  p1a_out_w("P", prim, pw);
  p1a_out_w("I", gwd, 13);
  p1a_out_w(0, in, 6);
  printf(" %d %d %d", ip, ifeat, niter);
  int r = ((p1a_fn_closest)VT(g)[P1A_FindClosest])(g, gwd, &ip, &ifeat, in, in + 3, out, niter);
  printf(" O %d %d %d", r, ip, ifeat);
  p1a_out_w(0, out, 6);
  printf("\n");
  p1a_release(g);
}

static double cp_drand(double lo, double hi) {
  return lo + (hi - lo) * ((double)(splitmix() >> 11) * (1.0 / 9007199254740992.0));
}

static void cp_crtd(const Toks *t) {
  typedef double (*d1)(double);
  typedef double (*d2)(double, double);
  const char *fn = need(t, "fn");
  HMODULE crt = GetModuleHandleA("MSVCR100.dll");
  void *f = (void *)GetProcAddress(crt, fn);
  int two = !strcmp(fn, "atan2");
  if (!f) die("CRT function not found", fn);
  const char *xs = get(t, "x", 0);
  int n = xs ? 1 : atoi(get(t, "n", "100"));
  double lo = atof(get(t, "lo", "-1")), hi = atof(get(t, "hi", "1"));
  g_rng = strtoull(get(t, "seed", "1"), 0, 0);
  for (int i = 0; i < n; i++) {
    double x, y = 0, r;
    if (xs) {
      uint64_t a = strtoull(xs, 0, 16), b = strtoull(get(t, "y", "0"), 0, 16);
      memcpy(&x, &a, 8);
      memcpy(&y, &b, 8);
    } else {
      x = cp_drand(lo, hi);
      if (two) y = cp_drand(lo, hi);
    }
    r = two ? ((d2)f)(x, y) : ((d1)f)(x);
    uint64_t a, b, w;
    memcpy(&a, &x, 8); memcpy(&b, &y, 8); memcpy(&w, &r, 8);
    if (two) printf("crtd %s %016llx %016llx %016llx\n", fn, (unsigned long long)a, (unsigned long long)b, (unsigned long long)w);
    else printf("crtd %s %016llx %016llx\n", fn, (unsigned long long)a, (unsigned long long)w);
  }
}

static int p1a_cmd_closest(const char *c, const Toks *t) {
  if (!strcmp(c, "crtd")) { cp_crtd(t); return 1; }
  if (strcmp(c, "cprobe")) return 0;
  int type = p1a_type_id(need(t, "type"));
  int n = atoi(get(t, "n", "100"));
  int dist = atoi(get(t, "dist", "0"));
  int mode = atoi(get(t, "mode", "0"));
  g_rng = strtoull(get(t, "seed", "1"), 0, 0);
  for (int i = 0; i < n; i++) cp_case(type, dist, mode, i);
  return 1;
}
