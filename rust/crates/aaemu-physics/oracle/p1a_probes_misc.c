/* P1a probes for src/geometry/shapes/misc.rs, #included by p1a_probes.c (after its helpers).
 *
 * Slots 20 GetFeature, 43 GetRandomPos, 51 (occlusion-cubemap rasteriser), 15 DrawWireframe and
 * 24 GetMemoryStatistics of the primitives, plus the internal polygon rasteriser sub_35131AB0 and
 * xlcommon's CryRandom (the generator slot 43 draws from). All lines use the `gp` format of
 * p1a_probes.c:
 *
 *   gfeat n= seed=                         box slot 20 on structured and random feature codes
 *   gcryrand n= seed=                      CryRandomSeed(seed), then n x (CryRandom, CryRandomFloat)
 *   grandpos type= n= seed= [form=F]       slot 43, two calls per case after CryRandomSeed
 *   grast n= seed= [res=R]                 sub_35131AB0 on random convex polygons
 *   gcube type= n= seed= [res=R]           slot 51 (the leaf worker of slot 23)
 *   gwire type= n= seed=                   slot 15 into a recording IPhysRenderer
 *   gmem type=                             slot 24 into a recording ICrySizer
 *
 * Cubemap pixels start from p1a_cube_fill(fill, k) (reproduced by the Rust tests); the output is
 * the list of (pixel index, new value) pairs that changed.
 */

static uint64_t splitmix(void);
double __cdecl sin(double);
double __cdecl cos(double);
double __cdecl fabs(double);

typedef int (*p1a_fn_feat)(void *, int, int, float *);
typedef void (*p1a_fn_rpos)(void *, float *, void *, int);
typedef void (*p1a_fn_seed)(unsigned);
typedef unsigned (*p1a_fn_cryrand)(void);
typedef float (*p1a_fn_cryrandf)(void);
typedef void (*p1a_fn_rast)(float *, int, unsigned, uint32_t **, int, float, float, float);
typedef int (*p1a_fn_cube)(void *, float *, int, int, unsigned, uint32_t **, int, float, float, float);
typedef void (*p1a_fn_wire)(void *, void *, float *, int, int);
typedef void (*p1a_fn_mem)(void *, void *);

enum { P1A_DrawWire = 15, P1A_MemStat = 24, P1A_RandomPos = 43, P1A_Cubemap = 51 };

static void *p1a_xl(const char *name) {
  HMODULE h = GetModuleHandleA("xlcommon.dll");
  void *p = h ? (void *)GetProcAddress(h, name) : 0;
  if (!p) die("xlcommon export not found", name);
  return p;
}

static uint32_t p1a_cube_fill(uint32_t fill, uint32_t k) {
  uint32_t x = k * 0x9E3779B1u ^ fill;
  x ^= x >> 16;
  x *= 0x85EBCA6Bu;
  x ^= x >> 13;
  switch (fill & 3) {
  case 0: return 0x7FFFFFFFu;
  case 1: return x;
  case 2: return x & 0x8000FFFFu;
  default: return (x & 0x80000000u) | 0x7FFFFFFFu;
  }
}

#define P1A_MAXRES 16
static uint32_t g_p1a_cube[6][P1A_MAXRES * P1A_MAXRES];
static uint32_t g_p1a_cube0[6][P1A_MAXRES * P1A_MAXRES];
static uint32_t *g_p1a_cubeptr[6];

static void p1a_cube_init(uint32_t fill, int res) {
  for (int f = 0; f < 6; f++) {
    g_p1a_cubeptr[f] = g_p1a_cube[f];
    for (int k = 0; k < P1A_MAXRES * P1A_MAXRES; k++)
      g_p1a_cube[f][k] = g_p1a_cube0[f][k] = p1a_cube_fill(fill, (uint32_t)(f * res * res + k));
  }
}
/* 1 if the call wrote outside the res x res pixels of a face (into the rest of the 16 x 16 buffer) */
static int p1a_cube_oob(int res) {
  for (int f = 0; f < 6; f++)
    for (int k = res * res; k < P1A_MAXRES * P1A_MAXRES; k++)
      if (g_p1a_cube[f][k] != g_p1a_cube0[f][k]) return 1;
  return 0;
}
static void p1a_cube_diff(int res) {
  int n = 0;
  if (p1a_cube_oob(res)) { printf(" oob"); return; }
  for (int f = 0; f < 6; f++)
    for (int k = 0; k < P1A_MAXRES * P1A_MAXRES; k++) n += g_p1a_cube[f][k] != g_p1a_cube0[f][k];
  printf(" %d", n);
  for (int f = 0; f < 6; f++)
    for (int k = 0; k < P1A_MAXRES * P1A_MAXRES; k++)
      if (g_p1a_cube[f][k] != g_p1a_cube0[f][k]) printf(" %d %08x", f * res * res + k, g_p1a_cube[f][k]);
}

static int p1a_pick_res(int res) {
  static const int rs[] = {1, 2, 3, 4, 5, 7, 8, 12, 16};
  return res > 0 ? res : rs[(int)frand(0, 8.999f)];
}
/* rmin, rmax, zscale as slot 23 derives them (zscale = 65535/rmax), sometimes arbitrary */
static void p1a_pick_depth(float *d) {
  d[1] = frand(0.5f, 30);
  d[0] = frand(0, 1) < 0.2f ? 0.0f : frand(0, d[1] * 0.5f);
  d[2] = frand(0, 1) < 0.8f ? 65535.0f / d[1] : frand(1, 5000);
}

/* random convex polygon (planar, ordered) somewhere around the cubemap origin */
/* kinds: 16 zigzag (only), 1 wild (non-planar, around the origin), 2 tiny (1e-5 guards), 4 axis-aligned with exact
   zeros, 8 degenerate (antipodal / repeated directions / the origin itself); anything else and
   disabled kinds give an ordinary convex polygon */
static int p1a_rand_poly(float *pts, int kinds) {
  float c[3], n[3], u[3], v[3];
  int np = 3 + (int)frand(0, 5.999f);
  float r = frand(0, 1);
  if (kinds & 16) { /* zigzag: 10 points alternating around +a / -a (many border crossings) */
    int a = (int)frand(0, 2.999f);
    np = 10;
    for (int i = 0; i < np; i++) {
      float sg = (i & 1) ? -1.0f : 1.0f;
      for (int k = 0; k < 3; k++) pts[3 * i + k] = frand(-1.2f, 1.2f);
      pts[3 * i + a] = sg * frand(0.8f, 1.5f);
    }
    return np;
  }
  if (r < 0.06f && (kinds & 8)) {
    np = 3 + (int)frand(0, 7.999f);
    for (int i = 0; i < np; i++) {
      float q = frand(0, 1);
      if (i > 0 && q < 0.35f) { /* opposite (or same) direction as an earlier vertex */
        int j = (int)frand(0, (float)i - 0.001f);
        float k = frand(0, 1) < 0.7f ? -frand(0.2f, 3) : frand(0.2f, 3);
        for (int a = 0; a < 3; a++) pts[3 * i + a] = pts[3 * j + a] * k;
      } else if (q < 0.45f) {
        memset(pts + 3 * i, 0, 12);
      } else {
        p1a_rand_unit(pts + 3 * i);
        float d = frand(0.1f, 5);
        for (int a = 0; a < 3; a++) pts[3 * i + a] *= d;
      }
    }
    return np;
  }
  if (r < 0.12f && (kinds & 1)) { /* wild: non-planar points all around the origin, up to 10 */
    np = 3 + (int)frand(0, 7.999f);
    for (int i = 0; i < np; i++) {
      p1a_rand_unit(pts + 3 * i);
      float d = frand(0.1f, 5);
      for (int k = 0; k < 3; k++) pts[3 * i + k] *= d;
    }
    return np;
  }
  if (r >= 0.12f && r < 0.17f && (kinds & 2)) { /* tiny, around the 1e-5 guards */
    float sc = frand(0, 1) < 0.5f ? 1e-6f : 2e-5f;
    for (int i = 0; i < np; i++)
      for (int k = 0; k < 3; k++) pts[3 * i + k] = frand(-1, 1) * sc;
    return np;
  }
  if (r >= 0.17f && r < 0.25f && (kinds & 4)) { /* axis-aligned polygon with exact zeros / diagonal ties */
    int a = (int)frand(0, 2.999f), b = (a + 1) % 3, e = (a + 2) % 3;
    float h = frand(0, 1) < 0.5f ? 0.0f : frand(-3, 3);
    float w = frand(0.5f, 6), off = frand(0, 1) < 0.5f ? 0.0f : frand(-2, 2);
    np = 4;
    float sg[4][2] = {{1, 1}, {-1, 1}, {-1, -1}, {1, -1}};
    for (int i = 0; i < 4; i++) {
      pts[3 * i + a] = h;
      pts[3 * i + b] = sg[i][0] * w + off;
      pts[3 * i + e] = sg[i][1] * (frand(0, 1) < 0.5f ? w : frand(0.1f, 6));
    }
    return np;
  }
  r = frand(0, 1);
  if (r < 0.15f) { /* near a cube edge / corner direction */
    for (int k = 0; k < 3; k++) c[k] = frand(0, 1) < 0.5f ? -1.0f : 1.0f;
    c[(int)frand(0, 2.999f)] *= frand(0, 1) < 0.5f ? frand(0.9f, 1.1f) : 1.0f;
    if (frand(0, 1) < 0.5f) c[(int)frand(0, 2.999f)] = frand(-0.2f, 0.2f);
    float d = frand(0.1f, 8);
    for (int k = 0; k < 3; k++) c[k] *= d;
  } else {
    p1a_rand_unit(c);
    float d = frand(0.05f, 12);
    for (int k = 0; k < 3; k++) c[k] *= d;
  }
  r = frand(0, 1);
  if (r < 0.2f) { memset(n, 0, 12); n[(int)frand(0, 2.999f)] = 1; }
  else p1a_rand_unit(n);
  /* u = normalize(n x a), v = n x u */
  float a[3] = {0, 0, 0};
  a[fabs(n[0]) < 0.6 ? 0 : 1] = 1;
  u[0] = n[1] * a[2] - n[2] * a[1]; u[1] = n[2] * a[0] - n[0] * a[2]; u[2] = n[0] * a[1] - n[1] * a[0];
  float l = 1.0f / sqrtf(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
  for (int k = 0; k < 3; k++) u[k] *= l;
  v[0] = n[1] * u[2] - n[2] * u[1]; v[1] = n[2] * u[0] - n[0] * u[2]; v[2] = n[0] * u[1] - n[1] * u[0];
  float cl = sqrtf(c[0] * c[0] + c[1] * c[1] + c[2] * c[2]);
  float rad = frand(0, 1) < 0.3f ? frand(0.001f, 0.2f) * cl : frand(0.2f, 4) * cl;
  float ang[8];
  for (int i = 0; i < np; i++) ang[i] = frand(0, 6.2831853f);
  if (frand(0, 1) < 0.3f) for (int i = 0; i < np; i++) ang[i] = 6.2831853f * i / np;
  for (int i = 1; i < np; i++) /* sort */
    for (int j = i; j > 0 && ang[j] < ang[j - 1]; j--) { float t = ang[j]; ang[j] = ang[j - 1]; ang[j - 1] = t; }
  for (int i = 0; i < np; i++) {
    float cs = (float)cos(ang[i]) * rad, sn = (float)sin(ang[i]) * rad;
    for (int k = 0; k < 3; k++) pts[3 * i + k] = c[k] + u[k] * cs + v[k] * sn;
  }
  if (frand(0, 1) < 0.05f) memcpy(pts + 3, pts, 12); /* repeated vertex */
  return np;
}

/* An access violation inside a guarded call (the rasteriser on degenerate, non-planar input)
   resumes after the RtlCaptureContext of the call site instead of reaching physoracle's
   exiting handler. */
static volatile int g_p1a_crashed, g_p1a_catching;
static __declspec(align(16)) CONTEXT g_p1a_ctx;
static LONG CALLBACK p1a_veh(PEXCEPTION_POINTERS e) {
  if (!g_p1a_catching || e->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION)
    return EXCEPTION_CONTINUE_SEARCH;
  g_p1a_catching = 0;
  g_p1a_crashed = 1;
  *e->ContextRecord = g_p1a_ctx;
  return EXCEPTION_CONTINUE_EXECUTION;
}
static void p1a_catch_init(void) {
  static int done;
  if (!done) { AddVectoredExceptionHandler(1, p1a_veh); done = 1; }
}

/* The rasteriser runs on a worker thread so that degenerate input that makes the original loop
   forever can be abandoned (TerminateThread after timeout_ms; the thread holds no locks). */
static struct {
  p1a_fn_rast rast;
  float *pts, d[3];
  int np, res;
  unsigned pass;
} g_p1a_job;
static DWORD WINAPI p1a_rast_thread(LPVOID x) {
  (void)x;
  g_p1a_crashed = 0;
  RtlCaptureContext(&g_p1a_ctx);
  if (!g_p1a_crashed) {
    g_p1a_catching = 1;
    g_p1a_job.rast(g_p1a_job.pts, g_p1a_job.np, g_p1a_job.pass, g_p1a_cubeptr, g_p1a_job.res, g_p1a_job.d[0],
                   g_p1a_job.d[1], g_p1a_job.d[2]);
    g_p1a_catching = 0;
  }
  return 0;
}
static int p1a_run_guarded(DWORD timeout_ms) {
  p1a_catch_init();
  HANDLE th = CreateThread(0, 1 << 20, p1a_rast_thread, 0, 0, 0);
  if (!th) die("CreateThread failed", 0);
  int hung = WaitForSingleObject(th, timeout_ms) != WAIT_OBJECT_0;
  if (hung) {
    TerminateThread(th, 1);
    WaitForSingleObject(th, INFINITE);
    g_p1a_catching = 0;
  }
  CloseHandle(th);
  return hung;
}

/* ---- recording IPhysRenderer / ICrySizer ---- */
static void *g_p1a_rec_geom;
static float *g_p1a_rec_gwd;
static void p1a_rec_generic(int slot, void *self, void *a, uintptr_t b) {
  (void)self;
  printf(" call%d %d %llx", slot, a == g_p1a_rec_geom, (unsigned long long)b);
}
#define P1A_REC_STUB(n) \
  static void p1a_rec_s##n(void *s, void *a, uintptr_t b) { p1a_rec_generic(n, s, a, b); }
P1A_REC_STUB(0) P1A_REC_STUB(1) P1A_REC_STUB(2) P1A_REC_STUB(3) P1A_REC_STUB(4) P1A_REC_STUB(5)
P1A_REC_STUB(6) P1A_REC_STUB(7) P1A_REC_STUB(8) P1A_REC_STUB(9) P1A_REC_STUB(10) P1A_REC_STUB(11)
P1A_REC_STUB(12) P1A_REC_STUB(13) P1A_REC_STUB(14) P1A_REC_STUB(15)
static void *g_p1a_recvt[16] = {
  (void *)p1a_rec_s0, (void *)p1a_rec_s1, (void *)p1a_rec_s2, (void *)p1a_rec_s3,
  (void *)p1a_rec_s4, (void *)p1a_rec_s5, (void *)p1a_rec_s6, (void *)p1a_rec_s7,
  (void *)p1a_rec_s8, (void *)p1a_rec_s9, (void *)p1a_rec_s10, (void *)p1a_rec_s11,
  (void *)p1a_rec_s12, (void *)p1a_rec_s13, (void *)p1a_rec_s14, (void *)p1a_rec_s15};
/* IPhysRenderer slot 1 DrawGeometry(geom, gwd, idxColor, bSlowFadein, sweepDir&) */
static void p1a_rec_drawgeom(void *s, void *geom, float *gwd, int color, int slow, float *sweep) {
  (void)s;
  printf(" geom %d %d %d %d", geom == g_p1a_rec_geom, gwd == g_p1a_rec_gwd, color, slow);
  p1a_out_w(0, sweep, 3);
}
/* IPhysRenderer slot 2 DrawLine(pt0, pt1, idxColor, bSlowFadein) */
static void p1a_rec_drawline(void *s, float *p0, float *p1, int color, int slow) {
  (void)s;
  printf(" line %d %d", color, slow);
  p1a_out_w(0, p0, 3);
  p1a_out_w(0, p1, 3);
}
static void *g_p1a_rendvt[16];

static int p1a_cmd_misc(const char *c, const Toks *t) {
  if (strcmp(c, "gfeat") && strcmp(c, "gcryrand") && strcmp(c, "grandpos") && strcmp(c, "grast") &&
      strcmp(c, "gcube") && strcmp(c, "gwire") && strcmp(c, "gmem"))
    return 0;
  int n = atoi(get(t, "n", "100"));
  g_rng = strtoull(get(t, "seed", "1"), 0, 0);
  if (!strcmp(c, "gfeat")) { /* box slot 20 */
    for (int i = 0; i < n; i++) {
      __declspec(align(16)) float prim[32], buf[16];
      p1a_rand_prim(0, prim);
      void *g = p1a_create(0, prim);
      int f;
      float r = frand(0, 1);
      if (r < 0.15f) f = (int)(uint32_t)splitmix();
      else if (r < 0.3f) f = (int)frand(0, 255.99f);
      else f = ((int)frand(0, 2.999f) << 5) | (int)frand(0, 15.99f);
      for (int k = 0; k < 16; k++) buf[k] = frand(-5, 5);
      int ip = (int)frand(-2, 3);
      printf("gp feat 0");
      p1a_out_w("P", prim, 16);
      printf(" I %d %d", ip, f);
      p1a_out_w(0, (char *)g + 112, 1);
      p1a_out_w(0, buf, 16);
      int nr = ((p1a_fn_feat)VT(g)[P1A_GetFeature])(g, ip, f, buf);
      printf(" O %d", nr);
      p1a_out_w(0, buf, 16);
      printf("\n");
      p1a_release(g);
    }
  } else if (!strcmp(c, "gcryrand")) {
    unsigned seed = (unsigned)strtoul(get(t, "rseed", "12345"), 0, 0);
    ((p1a_fn_seed)p1a_xl("?CryRandomSeed@@YAXI@Z"))(seed);
    printf("gp cryrand 0 P I %u O", seed);
    for (int i = 0; i < n; i++) {
      unsigned u = ((p1a_fn_cryrand)p1a_xl("?CryRandom@@YAIXZ"))();
      float f = ((p1a_fn_cryrandf)p1a_xl("?CryRandomFloat@@YAMXZ"))();
      printf(" %08x %08x", u, fbits(f));
    }
    printf("\n");
  } else if (!strcmp(c, "grandpos")) { /* slot 43 */
    int type = p1a_type_id(need(t, "type"));
    const char *fs = get(t, "form", "any");
    p1a_fn_seed seedf = (p1a_fn_seed)p1a_xl("?CryRandomSeed@@YAXI@Z");
    for (int i = 0; i < n; i++) {
      __declspec(align(16)) float prim[32], out[16];
      p1a_rand_prim(type, prim);
      if (type == 0 && frand(0, 1) < 0.1f) prim[13 + (int)frand(0, 2.999f)] = 0; /* flat box */
      void *g = p1a_create(type, prim);
      int form = strcmp(fs, "any") ? atoi(fs) : (int)frand(-1, 4.999f);
      unsigned rs = (unsigned)splitmix();
      printf("gp rpos %d", type);
      p1a_out_w("P", prim, p1a_prim_words(type));
      printf(" I %u %d O", rs, form);
      seedf(rs);
      for (int k = 0; k < 2; k++) {
        memset(out, 0xCD, sizeof out);
        ((p1a_fn_rpos)VT(g)[P1A_RandomPos])(g, out, 0, form);
        p1a_out_w(0, out, 6);
      }
      printf("\n");
      p1a_release(g);
    }
  } else if (!strcmp(c, "grast")) { /* sub_35131AB0 */
    int rq = atoi(get(t, "res", "0"));
    int kinds = (int)strtol(get(t, "kinds", "0xF"), 0, 0);
    DWORD timeout_ms = (DWORD)atoi(get(t, "timeout", "20000"));
    p1a_fn_rast rast = (p1a_fn_rast)VA(g_phys, 0x35131AB0ULL);
    for (int i = 0; i < n; i++) {
      __declspec(align(16)) float pts[48], d[3];
      int np = p1a_rand_poly(pts, kinds);
      int res = p1a_pick_res(rq);
      unsigned pass = frand(0, 1) < 0.5f;
      p1a_pick_depth(d);
      uint32_t fill = (uint32_t)splitmix();
      p1a_cube_init(fill, res);
      printf("gp rast 0 P I %u %d %u", pass, res, fill);
      p1a_out_w(0, d, 3);
      printf(" %d", np);
      p1a_out_w(0, pts, 3 * np);
      g_p1a_job.rast = rast;
      g_p1a_job.pts = pts;
      g_p1a_job.np = np;
      g_p1a_job.pass = pass;
      g_p1a_job.res = res;
      memcpy(g_p1a_job.d, d, 12);
      int hung = p1a_run_guarded(timeout_ms);
      printf(" O");
      if (hung) printf(" hang");
      else if (g_p1a_crashed) printf(" crash");
      else p1a_cube_diff(res);
      printf("\n");
    }
  } else if (!strcmp(c, "gcube")) { /* slot 51 */
    int type = p1a_type_id(need(t, "type"));
    int rq = atoi(get(t, "res", "0"));
    for (int i = 0; i < n; i++) {
      __declspec(align(16)) float prim[32], gwd[32], d[3];
      p1a_rand_prim(type, prim);
      void *g = p1a_create(type, prim);
      p1a_rand_gwd(gwd, 0);
      float dist = frand(0, 1) < 0.2f ? frand(0, 1) : frand(1, 12);
      float dir[3];
      p1a_rand_unit(dir);
      for (int k = 0; k < 3; k++) gwd[k] = dir[k] * dist;
      int res = p1a_pick_res(rq);
      unsigned pass = frand(0, 1) < 0.5f;
      p1a_pick_depth(d);
      uint32_t fill = (uint32_t)splitmix();
      p1a_cube_init(fill, res);
      printf("gp cube %d", type);
      p1a_out_w("P", prim, p1a_prim_words(type));
      printf(" I %u %d %u", pass, res, fill);
      p1a_out_w(0, d, 3);
      p1a_out_w(0, gwd, 13);
      int r = ((p1a_fn_cube)VT(g)[P1A_Cubemap])(g, gwd, 0, 1, pass, g_p1a_cubeptr, res, d[0], d[1], d[2]);
      printf(" O %d", r);
      p1a_cube_diff(res);
      printf("\n");
      p1a_release(g);
    }
  } else if (!strcmp(c, "gwire")) { /* slot 15 */
    int type = p1a_type_id(need(t, "type"));
    memcpy(g_p1a_rendvt, g_p1a_recvt, sizeof g_p1a_rendvt);
    g_p1a_rendvt[1] = (void *)p1a_rec_drawgeom;
    g_p1a_rendvt[2] = (void *)p1a_rec_drawline;
    void *rend = g_p1a_rendvt;
    for (int i = 0; i < n; i++) {
      __declspec(align(16)) float prim[32], gwd[32];
      p1a_rand_prim(type, prim);
      void *g = p1a_create(type, prim);
      p1a_rand_gwd(gwd, 0);
      int level = (int)frand(0, 3.999f), color = (int)frand(-3, 20);
      g_p1a_rec_geom = g;
      g_p1a_rec_gwd = gwd;
      printf("gp wire %d", type);
      p1a_out_w("P", prim, p1a_prim_words(type));
      printf(" I %d %d", level, color);
      p1a_out_w(0, gwd, 13);
      printf(" O");
      ((p1a_fn_wire)VT(g)[P1A_DrawWire])(g, &rend, gwd, level, color);
      printf("\n");
      p1a_release(g);
    }
  } else if (!strcmp(c, "gmem")) { /* slot 24 */
    int type = p1a_type_id(need(t, "type"));
    __declspec(align(16)) float prim[32];
    void *sizer = g_p1a_recvt;
    p1a_rand_prim(type, prim);
    void *g = p1a_create(type, prim);
    g_p1a_rec_geom = g;
    printf("gp mem %d", type);
    p1a_out_w("P", prim, p1a_prim_words(type));
    printf(" I O");
    ((p1a_fn_mem)VT(g)[P1A_MemStat])(g, &sizer);
    printf("\n");
    p1a_release(g);
  } else {
    return 0;
  }
  return 1;
}
