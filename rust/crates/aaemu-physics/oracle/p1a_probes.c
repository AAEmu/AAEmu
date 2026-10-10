/* P1a (primitive geometries) probes for physoracle, #included by physoracle.c.
 *
 * Owned by package P1a of aaemu-physics (see the crate README). The `gprobe` command creates
 * random primitives with the real CGeomManager::CreatePrimitive (box 0, ray 3, sphere 4,
 * cylinder 5, capsule 6), calls one geometry vtable slot on random inputs and prints every case
 * as one line of raw IEEE words:
 *
 *   gp <probe> <type> P <primitive words> I <input words> O <output words>
 *
 * The Rust tests (tests/p1a_*.rs) parse these lines, rebuild the primitive with the port, replay
 * the inputs and compare the outputs bit for bit. Inputs are printed, so the Rust side never has
 * to reproduce the random generator.
 *
 *   gprobe probe=NAME type=box|sphere|cylinder|capsule|ray n=COUNT seed=SEED [mode=N]
 *
 * Slot numbers and argument layouts: rust/docs/physics/cryphysics/geometry.md section 2.1.
 */

static float frand(float lo, float hi);
static void rand_quat(float *q);
static uint64_t g_rng;

typedef int (*p1a_fn_i)(void *);
typedef float (*p1a_fn_f)(void *);
typedef void (*p1a_fn_vp)(void *, void *);
typedef void *(*p1a_fn_pp)(void *, void *);
typedef int (*p1a_fn_ip)(void *, void *);
typedef int (*p1a_fn_iip)(void *, int, void *);
typedef int (*p1a_fn_iiip)(void *, int, int, void *);
typedef int (*p1a_fn_if)(void *, float);
typedef float (*p1a_fn_fppp)(void *, void *, void *, void *);
typedef void (*p1a_fn_vpppp)(void *, void *, void *, void *, void *);
typedef int (*p1a_fn_closest)(void *, void *, int *, int *, float *, float *, float *, int);
typedef void (*p1a_fn_press)(void *, void *, float *, float, float, float *, float *, float *);
typedef float (*p1a_fn_extent)(void *, int, int);
typedef int (*p1a_fn_prep)(void *, void *, void **, int);
typedef void (*p1a_fn_q2m)(float *, const float *);

enum {
  P1A_GetType = 1, P1A_Release = 3, P1A_GetBBox = 6, P1A_CalcPhysProps = 7, P1A_PointInside = 8,
  P1A_FindClosest = 11, P1A_VolPressure = 12, P1A_Buoyancy = 13, P1A_MediumRes = 14, P1A_GetPrimitive = 17,
  P1A_GetFeature = 20, P1A_IsConvex = 21, P1A_PrepRayTest = 22, P1A_Save = 25, P1A_Load = 27, P1A_PrimCount = 28,
  P1A_GetVolume = 31, P1A_GetCenter = 32, P1A_GetExtent = 42, P1A_IsAPrimitive = 47, P1A_PreparePrim = 48,
  P1A_SizeOf = 52
};

static int p1a_prim_words(int type) {
  switch (type) {
  case 0: return 16;
  case 3: return 6;
  case 4: return 4;
  default: return 8;
  }
}
static void p1a_out_w(const char *tag, const void *p, int n) {
  if (tag) printf(" %s", tag);
  for (int i = 0; i < n; i++) printf(" %08x", ((const uint32_t *)p)[i]);
}
static void p1a_rand_unit(float *v) {
  float n2;
  do {
    for (int i = 0; i < 3; i++) v[i] = frand(-1, 1);
    n2 = v[0] * v[0] + v[1] * v[1] + v[2] * v[2];
  } while (n2 < 0.01f || n2 > 1.0f);
  float inv = 1.0f / sqrtf(n2);
  for (int i = 0; i < 3; i++) v[i] *= inv;
}
static void p1a_rand_rot(float *m) {
  float q[4];
  rand_quat(q);
  ((p1a_fn_q2m)VA(g_phys, 0x350247B0ULL))(m, q);
}
static void p1a_identity(float *m) {
  memset(m, 0, 36);
  m[0] = m[4] = m[8] = 1;
}
static void p1a_rand_axis(float *a) {
  float r = frand(0, 1);
  memset(a, 0, 12);
  if (r < 0.25f) a[2] = 1;
  else if (r < 0.45f) a[(int)frand(0, 2.999f)] = frand(0, 1) < 0.5f ? -1.0f : 1.0f;
  else p1a_rand_unit(a);
}
/* random primitive of `type`, written in the CreatePrimitive layout; returns the bounding radius */
static float p1a_rand_prim(int type, float *p) {
  memset(p, 0, 64);
  switch (type) {
  case 0: {
    float r = frand(0, 1);
    if (r < 0.35f) p1a_identity(p);
    else p1a_rand_rot(p);
    ((uint32_t *)p)[9] = 0x12345678; /* bOriented is recomputed by SetData */
    for (int i = 0; i < 3; i++) p[10 + i] = frand(-2, 2);
    for (int i = 0; i < 3; i++) p[13 + i] = frand(0.05f, 4);
    if (frand(0, 1) < 0.1f) p[13 + (int)frand(0, 2.999f)] = 0.01f;
    return sqrtf(p[13] * p[13] + p[14] * p[14] + p[15] * p[15]);
  }
  case 3: {
    for (int i = 0; i < 3; i++) p[i] = frand(-5, 5);
    if (frand(0, 1) >= 0.08f)
      for (int i = 0; i < 3; i++) p[3 + i] = frand(-10, 10);
    return sqrtf(p[3] * p[3] + p[4] * p[4] + p[5] * p[5]) * 0.5f + 0.01f;
  }
  case 4:
    for (int i = 0; i < 3; i++) p[i] = frand(-2, 2);
    p[3] = frand(0.05f, 4);
    return p[3];
  default:
    for (int i = 0; i < 3; i++) p[i] = frand(-2, 2);
    p1a_rand_axis(p + 3);
    p[6] = frand(0.05f, 3);
    p[7] = frand(0.05f, 4);
    return type == 6 ? p[6] + p[7] : sqrtf(p[6] * p[6] + p[7] * p[7]);
  }
}
static void *p1a_create(int type, float *prim) {
  void *g = ((fn_prim)VT(g_gm)[G_CreatePrimitive])(g_gm, type, prim);
  if (!g) die("CreatePrimitive failed", 0);
  return g;
}
static void p1a_release(void *g) { ((p1a_fn_i)VT(g)[P1A_Release])(g); }

/* geom_world_data (88 B): offset, R[9], scale, v, w, centerOfMass */
static void p1a_rand_gwd(float *gwd, int mode) {
  memset(gwd, 0, 88);
  for (int i = 0; i < 3; i++) gwd[i] = frand(-10, 10);
  if (mode == 1 || frand(0, 1) < 0.25f) p1a_identity(gwd + 3);
  else p1a_rand_rot(gwd + 3);
  gwd[12] = (mode == 1 || frand(0, 1) < 0.5f) ? 1.0f : frand(0.25f, 3);
  for (int i = 0; i < 3; i++) gwd[13 + i] = frand(-5, 5);
  for (int i = 0; i < 3; i++) gwd[16 + i] = frand(-3, 3);
  for (int i = 0; i < 3; i++) gwd[19 + i] = gwd[i] + frand(-2, 2);
}
/* world position of the primitive's centre under gwd (single precision, only to aim the inputs) */
static void p1a_world_center(int type, const float *p, const float *gwd, float *c) {
  float l[3];
  if (type == 0) memcpy(l, p + 10, 12);
  else if (type == 3) for (int i = 0; i < 3; i++) l[i] = p[i] + p[3 + i] * 0.5f;
  else memcpy(l, p, 12);
  for (int i = 0; i < 3; i++)
    c[i] = gwd[i] + gwd[12] * (gwd[3 + 3 * i] * l[0] + gwd[4 + 3 * i] * l[1] + gwd[5 + 3 * i] * l[2]);
}
/* water plane {n, origin} cutting near the primitive */
static void p1a_rand_plane(float *pl, const float *c, float rad) {
  float r = frand(0, 1);
  if (r < 0.5f) { pl[0] = 0; pl[1] = 0; pl[2] = 1; }
  else if (r < 0.75f) p1a_rand_unit(pl);
  else {
    pl[0] = frand(-0.3f, 0.3f); pl[1] = frand(-0.3f, 0.3f); pl[2] = 1;
    float inv = 1.0f / sqrtf(pl[0] * pl[0] + pl[1] * pl[1] + 1);
    for (int i = 0; i < 3; i++) pl[i] *= inv;
  }
  float d = frand(-1.3f, 1.3f) * rad;
  for (int i = 0; i < 3; i++) pl[3 + i] = c[i] + pl[i] * d + frand(-1, 1);
}

/* fake CMemStream: vt, +8 buf, +20 pos, +24 size, +28 bDeleteBuf, +32 bCountOnly */
static unsigned char g_p1a_streambuf[1 << 16];
static void p1a_stream_grow(void *s, int n) { (void)s; (void)n; die("CMemStream grow (buffer too small)", 0); }
static void *g_p1a_streamvt[4] = {0, (void *)p1a_stream_grow, 0, 0};
static void p1a_stream_init(unsigned char *s, int count_only) {
  memset(s, 0, 64);
  *(void ***)s = g_p1a_streamvt;
  *(void **)(s + 8) = g_p1a_streambuf;
  *(int *)(s + 24) = (int)sizeof g_p1a_streambuf;
  *(int *)(s + 32) = count_only;
}

static void p1a_basic(void *g, int type) {
  __declspec(align(16)) unsigned char b[512];
  printf(" O %d", ((p1a_fn_i)VT(g)[P1A_GetType])(g));
  memset(b, 0xCD, sizeof b);
  ((p1a_fn_vp)VT(g)[P1A_GetBBox])(g, b);
  p1a_out_w("bbox", b, 16);
  memset(b, 0, sizeof b);
  int r = ((p1a_fn_ip)VT(g)[P1A_CalcPhysProps])(g, b);
  printf(" props %d", r);
  if (r) p1a_out_w(0, b + 8, 11); /* Ibody, q, origin, V */
  float v = ((p1a_fn_f)VT(g)[P1A_GetVolume])(g);
  p1a_out_w("vol", &v, 1);
  memset(b, 0, sizeof b);
  ((p1a_fn_pp)VT(g)[P1A_GetCenter])(g, b);
  p1a_out_w("center", b, 3);
  memset(b, 0xCD, sizeof b);
  int n = ((p1a_fn_iip)VT(g)[P1A_GetPrimitive])(g, 0, b);
  printf(" prim %d", n);
  p1a_out_w(0, b, n / 4);
  p1a_out_w("hdr", (char *)g + 16, 3); /* minVtxDist, priority, +24 */
  printf(" cnt %d conv %d isprim %d size %d", ((p1a_fn_i)VT(g)[P1A_PrimCount])(g),
         ((p1a_fn_if)VT(g)[P1A_IsConvex])(g, 0.02f), ((p1a_fn_i)VT(g)[P1A_IsAPrimitive])(g),
         ((p1a_fn_i)VT(g)[P1A_SizeOf])(g));
  printf(" ext");
  for (int f = 0; f < 4; f++) {
    float e = ((p1a_fn_extent)VT(g)[P1A_GetExtent])(g, f, f);
    printf(" %08x", fbits(e));
  }
  if (type == 3) p1a_out_w("ndir", (char *)g + 72, 3);
  if (type == 5 || type == 6) printf(" ntess %d", *(int *)((char *)g + 80));
}

static int p1a_type_id(const char *s) {
  if (!strcmp(s, "box")) return 0;
  if (!strcmp(s, "ray")) return 3;
  if (!strcmp(s, "sphere")) return 4;
  if (!strcmp(s, "cylinder")) return 5;
  if (!strcmp(s, "capsule")) return 6;
  die("p1a: unknown primitive type", s);
  return -1;
}

static void cmd_gprobe(const Toks *t) {
  const char *probe = need(t, "probe");
  int type = p1a_type_id(need(t, "type"));
  int n = atoi(get(t, "n", "100"));
  int mode = atoi(get(t, "mode", "0"));
  g_rng = strtoull(get(t, "seed", "1"), 0, 0);
  for (int i = 0; i < n; i++) {
    __declspec(align(16)) float prim[32], gwd[32], pl[8], in[64];
    __declspec(align(16)) float out[256];
    float rad = p1a_rand_prim(type, prim);
    int pw = p1a_prim_words(type);
    void *g = p1a_create(type, prim);
    printf("gp %s %d", probe, type);
    p1a_out_w("P", prim, pw);
    if (!strcmp(probe, "basic")) {
      printf(" I");
      p1a_basic(g, type);
    } else if (!strcmp(probe, "inside")) { /* slot 8 */
      float c[3], gw[32];
      p1a_identity(gw + 3);
      memset(gw, 0, 12);
      gw[12] = 1;
      p1a_world_center(type, prim, gw, c);
      for (int k = 0; k < 3; k++) in[k] = c[k] + frand(-1.3f, 1.3f) * rad;
      if (mode == 1 && type == 0) /* exactly on a face plane */
        for (int k = 0; k < 3; k++) in[k] = prim[10 + k] + prim[13 + k];
      p1a_out_w("I", in, 3);
      printf(" O %d", ((p1a_fn_ip)VT(g)[P1A_PointInside])(g, in));
    } else if (!strcmp(probe, "buoy")) { /* slot 13 */
      float c[3];
      p1a_rand_gwd(gwd, mode);
      p1a_world_center(type, prim, gwd, c);
      p1a_rand_plane(pl, c, rad * gwd[12]);
      p1a_out_w("I", pl, 6);
      p1a_out_w(0, gwd, 13);
      memset(out, 0, 64);
      float v = ((p1a_fn_fppp)VT(g)[P1A_Buoyancy])(g, pl, gwd, out);
      p1a_out_w("O", &v, 1);
      p1a_out_w(0, out, 3);
    } else if (!strcmp(probe, "res")) { /* slot 14 */
      float c[3];
      p1a_rand_gwd(gwd, mode);
      p1a_world_center(type, prim, gwd, c);
      p1a_rand_plane(pl, c, rad * gwd[12]);
      for (int k = 0; k < 6; k++) out[k] = (i % 3 == 0) ? 0.0f : frand(-2, 2); /* dP, dL in */
      p1a_out_w("I", pl, 6);
      p1a_out_w(0, gwd, 22);
      p1a_out_w(0, out, 6);
      ((p1a_fn_vpppp)VT(g)[P1A_MediumRes])(g, pl, gwd, out, out + 3);
      p1a_out_w("O", out, 6);
    } else if (!strcmp(probe, "press")) { /* slot 12 */
      float c[3];
      p1a_rand_gwd(gwd, mode);
      p1a_world_center(type, prim, gwd, c);
      for (int k = 0; k < 3; k++) in[k] = c[k] + frand(-3, 3) * (rad * gwd[12] + 0.5f); /* epicenter */
      in[3] = frand(0.1f, 100);                                                       /* k */
      in[4] = frand(0.01f, 2);                                                        /* rmin */
      for (int k = 0; k < 3; k++) in[5 + k] = gwd[19 + k];                            /* com */
      for (int k = 0; k < 6; k++) out[k] = (i % 3 == 0) ? 0.0f : frand(-2, 2);        /* P, L in */
      p1a_out_w("I", gwd, 13);
      p1a_out_w(0, in, 8);
      p1a_out_w(0, out, 6);
      ((p1a_fn_press)VT(g)[P1A_VolPressure])(g, gwd, in, in[3], in[4], in + 5, out, out + 3);
      p1a_out_w("O", out, 6);
    } else if (!strcmp(probe, "closest")) { /* slot 11 */
      float c[3];
      int ip, ifeat;
      p1a_rand_gwd(gwd, mode);
      p1a_world_center(type, prim, gwd, c);
      for (int k = 0; k < 3; k++) in[k] = c[k] + frand(-2, 2) * (rad * gwd[12] + 0.2f);
      if (i % 4 == 0) memcpy(in + 3, in, 12);
      else for (int k = 0; k < 3; k++) in[3 + k] = in[k] + frand(-2, 2) * (rad * gwd[12] + 0.2f);
      ip = (int)frand(-1, 2.99f);
      ifeat = (i % 2) ? -1 : (int)frand(0, 100);
      int niter = (int)frand(1, 20.99f);
      memset(out, 0, 64);
      p1a_out_w("I", gwd, 13);
      p1a_out_w(0, in, 6);
      printf(" %d %d %d", ip, ifeat, niter);
      int r = ((p1a_fn_closest)VT(g)[P1A_FindClosest])(g, gwd, &ip, &ifeat, in, in + 3, out, niter);
      printf(" O %d %d %d", r, ip, ifeat);
      p1a_out_w(0, out, 6);
    } else if (!strcmp(probe, "prep")) { /* slot 48 */
      void *pp = 0;
      p1a_rand_gwd(gwd, mode);
      p1a_out_w("I", gwd, 13);
      int r = ((p1a_fn_prep)VT(g)[P1A_PreparePrim])(g, gwd, &pp, 0);
      printf(" O %d", r);
      if (pp) p1a_out_w(0, pp, p1a_prim_words(r));
    } else if (!strcmp(probe, "feature")) { /* slot 20 */
      int ip = 0, f = (int)frand(0, 3.99f) << 5 | (int)frand(0, 11.99f);
      memset(out, 0xCD, 4 * 64);
      printf(" I %d %d", ip, f);
      int r = ((p1a_fn_iiip)VT(g)[P1A_GetFeature])(g, ip, f, out);
      printf(" O %d", r);
      p1a_out_w(0, out, 3 * (r > 0 && r < 16 ? r : 0));
    } else if (!strcmp(probe, "save")) { /* slots 25 / 27 and CGeomManager Save/LoadPhysGeometry */
      unsigned char s[64];
      p1a_stream_init(s, 0);
      ((p1a_fn_vp)VT(g)[P1A_Save])(g, s);
      int nb = *(int *)(s + 20);
      printf(" I O %d", nb);
      p1a_out_w(0, g_p1a_streambuf, nb / 4);
      /* load the bytes into a second object of the same type, then report its basic state */
      __declspec(align(16)) float prim2[32];
      p1a_rand_prim(type, prim2);
      void *g2 = p1a_create(type, prim2);
      p1a_stream_init(s, 0);
      ((p1a_fn_vp)VT(g2)[P1A_Load])(g2, s);
      printf(" load %d", *(int *)(s + 20));
      p1a_basic(g2, type);
      p1a_release(g2);
    } else {
      die("p1a: unknown probe", probe);
    }
    printf("\n");
    p1a_release(g);
  }
}

/* CGeomManager::LoadPhysGeometry (slot 16) on a CGF-style blob: u32 1, 64 B phys_geometry header,
   u32 type, primitive payload (as written by geometry slot 25). Prints the resulting
   phys_geometry (+8 Ibody, +20 q, +36 origin, +48 V, +52, +56 surface idx). */
typedef void *(*p1a_fn_loadphys)(void *, void *, void *, void *, void *);
static void cmd_gphysblob(const Toks *t) {
  int type = p1a_type_id(need(t, "type"));
  int n = atoi(get(t, "n", "20"));
  g_rng = strtoull(get(t, "seed", "1"), 0, 0);
  for (int i = 0; i < n; i++) {
    __declspec(align(16)) float prim[32];
    unsigned char s[64];
    p1a_rand_prim(type, prim);
    void *g = p1a_create(type, prim);
    uint32_t *w = (uint32_t *)g_p1a_streambuf;
    memset(g_p1a_streambuf, 0, 4096);
    w[0] = 1;
    for (int k = 1; k < 17; k++) w[k] = fbits(frand(-3, 3));
    w[2] = fbits(frand(0.001f, 50)); w[3] = fbits(frand(0.001f, 50)); w[4] = fbits(frand(0.001f, 50));
    if (i % 3 == 0) w[3] = fbits(frand(0, 0.01f));
    w[14] = (uint32_t)(int)frand(0, 100);
    w[17] = (uint32_t)type;
    p1a_stream_init(s, 0);
    *(int *)(s + 20) = 72;
    ((p1a_fn_vp)VT(g)[P1A_Save])(g, s);
    int nb = *(int *)(s + 20);
    p1a_release(g);
    printf("gb %d I", type);
    p1a_out_w(0, g_p1a_streambuf, nb / 4);
    p1a_stream_init(s, 0);
    int64_t a3[2] = {0, 0}, a4[2] = {0, 0};
    void *pg = ((p1a_fn_loadphys)VT(g_gm)[16])(g_gm, s, a3, a4, 0);
    printf(" O %d", *(int *)(s + 20));
    if (pg) {
      p1a_out_w(0, (char *)pg + 8, 13);
      p1a_out_w("geom", (char *)*(void **)pg + 16, 1);
    }
    printf("\n");
  }
}

static int p1a_command(const char *c, const Toks *t) {
  if (!strcmp(c, "gprobe")) cmd_gprobe(t);
  else if (!strcmp(c, "gphysblob")) cmd_gphysblob(t);
  else return 0;
  return 1;
}
