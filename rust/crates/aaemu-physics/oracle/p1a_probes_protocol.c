/* P1a probes for src/geometry/shapes/protocol.rs, #included by p1a_probes.c (after its helpers).
 *
 * The intersection-test protocol slots of the primitives (geometry.md section 2.1/2.3):
 * 49 UnprojectSphere, 53 PrepareForIntersectionTest, 54 RegisterIntersection (ray),
 * 56 GetPrimitiveList, 57 GetUnprojectionCandidates, 58/59 feature polygons.
 *
 *   gproto probe=prep53|list56|cand57|poly58|poly59|reg54|unproj49 type=... n=N seed=S [mode=M]
 *
 * Every case prints one `gp <probe> <type> P <prim words> I <inputs> O <outputs>` line (raw
 * words), parsed by tests/p1a_support. The geometry_under_test ("gtest", 392 bytes) records are
 * built here: the transform (+48 R, +120 offset, +144 scale) and iCaller (+388) are random, the
 * rest is filled by the real slot 53 or set field by field.
 */

typedef int (*p1p_fn53)(void *, void *, void *, void *, int);
typedef int (*p1p_fn56)(void *, int, int, int, void *, int, void *, void *, void *, void *);
typedef int (*p1p_fn57)(void *, int, void *, void **, int, void *);
typedef int (*p1p_fn58)(void *, float *, int, int, void *, void **, void **, void **);
typedef int (*p1p_fn54)(void *, void *, void *, void *, void *, void *);
typedef int (*p1p_fn49)(void *, float *, float, float, void *);
typedef int (*p1p_fnov)(void *, void *, void *);
typedef int (*p1p_fnun)(void *, void *, int, void *, int, void *, void *);

enum { P1P_Unproj = 49, P1P_Prep = 53, P1P_Reg = 54, P1P_List = 56, P1P_Cand = 57, P1P_PolyA = 58, P1P_PolyB = 59 };

#define P1P_GT 392
#define GT_I(gt, off) (*(int *)((char *)(gt) + (off)))
#define GT_F(gt, off) (*(float *)((char *)(gt) + (off)))
#define GT_P(gt, off) (*(void **)((char *)(gt) + (off)))

static unsigned char *p1p_arena(int ic) { return (unsigned char *)VA(g_phys, 0x35256F80ULL) + 237496LL * ic; }
/* the per-caller primitive ring counters (box +0x34EC, sphere +0x3468, cylinder +0x3444), reset by
   every Intersect call (0x3514FD70); slot 53 increments them without a mask */
static void p1p_reset_rings(int ic) {
  unsigned char *a = p1p_arena(ic);
  *(int *)(a + 13548) = 0;
  *(int *)(a + 13416) = 0;
  *(int *)(a + 13380) = 0;
}
/* random gtest transform: R (+48), offset (+120), scale (+144) and iCaller (+388) */
static void p1p_frame(unsigned char *gt, int mode, int ic) {
  float *R = (float *)(gt + 48);
  if (mode == 1 || frand(0, 1) < 0.25f) p1a_identity(R);
  else p1a_rand_rot(R);
  for (int k = 0; k < 3; k++) GT_F(gt, 120 + 4 * k) = frand(-10, 10);
  GT_F(gt, 144) = (mode == 1 || frand(0, 1) < 0.5f) ? 1.0f : frand(0.25f, 3);
  GT_I(gt, 388) = ic;
}
static void p1p_print_frame(const unsigned char *gt) {
  p1a_out_w(0, gt + 48, 9);
  p1a_out_w(0, gt + 120, 3);
  p1a_out_w(0, gt + 144, 1);
  printf(" %d", GT_I(gt, 388));
}
static int p1p_prep(void *g, unsigned char *gt) {
  p1p_reset_rings(GT_I(gt, 388));
  return ((p1p_fn53)VT(g)[P1P_Prep])(g, gt, g, gt, 0);
}
/* v = R*l*s + o in single precision (only to aim the generated inputs) */
static void p1p_xf(const unsigned char *gt, const float *l, float *v) {
  const float *R = (const float *)(gt + 48);
  for (int i = 0; i < 3; i++)
    v[i] = (R[3 * i] * l[0] + R[3 * i + 1] * l[1] + R[3 * i + 2] * l[2]) * GT_F(gt, 144) + GT_F(gt, 120 + 4 * i);
}
static void p1p_cross(const float *a, const float *b, float *c) {
  c[0] = a[1] * b[2] - a[2] * b[1];
  c[1] = a[2] * b[0] - a[0] * b[2];
  c[2] = a[0] * b[1] - a[1] * b[0];
}
static void p1p_norm(float *v) {
  float l = sqrtf(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
  if (l > 0) for (int k = 0; k < 3; k++) v[k] /= l;
}
static uint32_t p1p_rand_word(void) {
  uint32_t a = (uint32_t)(frand(0, 1) * 65536.0f), b = (uint32_t)(frand(0, 1) * 65536.0f);
  return a << 16 | b;
}

/* a point on / near the transformed primitive tp (layout of `type`) */
static void p1p_surface_point(int type, const float *tp, float *pt) {
  if (type == 0) {
    for (int k = 0; k < 3; k++) pt[k] = tp[10 + k];
    for (int a = 0; a < 3; a++) {
      float r = frand(0, 1), u;
      if (r < 0.3f) u = 1;
      else if (r < 0.5f) u = -1;
      else if (r < 0.65f) u = 1 + frand(-0.002f, 0.002f);
      else u = frand(-1, 1);
      for (int k = 0; k < 3; k++) pt[k] += tp[3 * a + k] * (u * tp[13 + a]);
    }
  } else if (type == 5 || type == 6) {
    float e[3], q[3], r = frand(0, 1), h, rr;
    p1a_rand_unit(q);
    p1p_cross(tp + 3, q, e);
    p1p_norm(e);
    h = r < 0.3f ? tp[7] : r < 0.45f ? -tp[7] : r < 0.6f ? tp[7] * (1 + frand(-0.001f, 0.001f)) : frand(-1.2f, 1.2f) * tp[7];
    r = frand(0, 1);
    rr = r < 0.4f ? tp[6] : r < 0.6f ? tp[6] * (1 + frand(-0.001f, 0.001f)) : frand(0, 1.2f) * tp[6];
    for (int k = 0; k < 3; k++) pt[k] = tp[k] + tp[3 + k] * h + e[k] * rr;
  } else {
    for (int k = 0; k < 3; k++) pt[k] = tp[k] + frand(-3, 3);
  }
}
static int p1p_rand_feature(int type) {
  float r = frand(0, 1);
  if (type == 0) {
    if (r < 0.3f) return 0x40 | (int)frand(0, 2.999f) << 1 | (int)frand(0, 1.999f);
    if (r < 0.6f) return 0x20 | (int)frand(0, 2.999f) << 2 | (int)frand(0, 3.999f);
    if (r < 0.85f) return (int)frand(0, 7.999f);
    return (int)frand(0, 255.999f);
  }
  if (r < 0.2f) return 0x40;
  if (r < 0.4f) return 0x41;
  if (r < 0.6f) return 0x42;
  if (r < 0.75f) return 0x20 | (int)frand(0, 1.999f);
  return (int)frand(0, 255.999f);
}

static void p1p_case(const char *probe, int type, int mode, int i, void *g, float *prim) {
  __declspec(align(16)) unsigned char gt[P1P_GT], gt2[P1P_GT];
  __declspec(align(16)) float buf[64], tp[64];
  int ic = (int)frand(0, 4.999f);
  int pw = p1a_prim_words(type);
  if (!strcmp(probe, "prep53")) {
    memset(gt, 0xA5, sizeof gt);
    p1p_frame(gt, mode, ic);
    printf(" I");
    p1p_print_frame(gt);
    int r = p1p_prep(g, gt);
    printf(" O %d %d %d %d", r, GT_I(gt, 280), GT_I(gt, 304), GT_I(gt, 328));
    p1a_out_w(0, gt + 360, 1);
    void *m = GT_P(gt, 16);
    printf(" %d %d %d %d", m == 0 ? 0 : m == gt + 32 ? 1 : 2, GT_P(gt, 24) != 0, GT_I(gt, 32), GT_I(gt, 36));
    printf(" %d %d %d", GT_P(gt, 288) == GT_P(gt, 296), GT_P(gt, 336) == 0, GT_P(gt, 344) == 0);
    p1a_out_w(0, gt + 40, 1);
    p1a_out_w(0, gt + 160, 1);
    p1a_out_w(0, gt + 352, 2);
    if (type == 3) {
      printf(" %d", *(signed char *)GT_P(gt, 320));
      p1a_out_w(0, GT_P(gt, 288), 6);
    }
  } else if (!strcmp(probe, "list56")) {
    memset(gt, 0, sizeof gt);
    memset(gt2, 0, sizeof gt2);
    p1p_frame(gt, mode, ic);
    p1p_prep(g, gt);
    GT_I(gt, 160) = (int)frand(0, 1.999f);
    /* the other side's outside pivot (+268) */
    float c[3], piv[3], r = frand(0, 1);
    p1p_xf(gt, type == 0 ? prim + 10 : prim, c);
    float ext = (type == 0 ? sqrtf(prim[13] * prim[13] + prim[14] * prim[14] + prim[15] * prim[15]) : 2.0f) * GT_F(gt, 144);
    for (int k = 0; k < 3; k++) piv[k] = c[k] + frand(-1.5f, 1.5f) * ext;
    if (r < 0.15f) piv[0] = 1e10f;
    else if (r < 0.2f) piv[0] = frand(1e10f, 1e12f);
    else if (r < 0.23f) piv[0] = bitsf(0x7FC00000u);
    memcpy(gt2 + 268, piv, 12);
    int ctype = (int)frand(0, 4.999f), bl = (int)frand(0, 1.999f);
    printf(" I");
    p1p_print_frame(gt);
    printf(" %d", GT_I(gt, 160));
    p1a_out_w(0, piv, 3);
    int ret = ((p1p_fn56)VT(g)[P1P_List])(g, 0, 1, ctype, buf, bl, gt, gt2, GT_P(gt, 288), GT_P(gt, 320));
    printf(" O %d", ret);
    p1a_out_w(0, GT_P(gt, 288), pw);
    printf(" %d %d", GT_I(gt, 160), *(signed char *)GT_P(gt, 320));
  } else if (!strcmp(probe, "cand57")) {
    memset(gt, 0, sizeof gt);
    memset(gt2, 0, sizeof gt2);
    p1p_frame(gt, mode, ic);
    p1p_prep(g, gt);
    /* the primitive in the gtest frame, through slot 56 into a private buffer */
    ((p1p_fn56)VT(g)[P1P_List])(g, 0, 1, 0, buf, 0, gt, gt2, tp, GT_P(gt, 320));
    int flag = (i % 3) != 0;
    GT_I(gt, 160) = flag;
    __declspec(align(16)) unsigned char ct[48];
    float pt[3];
    p1p_surface_point(type, tp, pt);
    *(double *)ct = frand(-1, 1);
    *(double *)(ct + 8) = frand(-1, 1);
    memcpy(ct + 16, pt, 12);
    for (int k = 0; k < 3; k++) ((float *)(ct + 28))[k] = frand(-1, 1);
    int iside = i & 1;
    ((int *)(ct + 40))[0] = p1p_rand_feature(type);
    ((int *)(ct + 40))[1] = p1p_rand_feature(type);
    void *pp;
    if (flag) {
      for (int k = 0; k < 16; k++) ((uint32_t *)buf)[k] = 0xDEADBEEF;
      pp = buf;
    } else {
      memcpy(buf, tp, 64);
      pp = buf;
    }
    GT_I(gt, 352) = 0x7777;
    GT_I(gt, 356) = 0x7777;
    printf(" I");
    p1p_print_frame(gt);
    printf(" %d %d", flag, iside);
    p1a_out_w(0, ct, 12);
    p1a_out_w(0, buf, pw);
    p1a_out_w(0, GT_P(gt, 296), pw);
    int ret = ((p1p_fn57)VT(g)[P1P_Cand])(g, iside, ct, &pp, 0, gt);
    printf(" O %d %d", ret, pp == GT_P(gt, 296));
    p1a_out_w(0, pp, pw);
    p1a_out_w(0, GT_P(gt, 296), pw);
    int nf = GT_I(gt, 352), ne = GT_I(gt, 356);
    printf(" %d %d %d", *(signed char *)GT_P(gt, 320), nf, ne);
    for (int k = 0; k < nf && k < 8; k++) p1a_out_w(0, (char *)GT_P(gt, 336) + 20 * k, 5);
    for (int k = 0; k < ne && k < 8; k++) p1a_out_w(0, (char *)GT_P(gt, 344) + 44 * k, 11);
  } else if (!strcmp(probe, "poly58") || !strcmp(probe, "poly59")) {
    int slot = !strcmp(probe, "poly58") ? P1P_PolyA : P1P_PolyB;
    memset(gt, 0, sizeof gt);
    p1p_frame(gt, mode, ic);
    p1p_prep(g, gt);
    float fr[12], c[3];
    p1p_xf(gt, type == 0 ? prim + 10 : prim, c);
    if (i % 3 == 0) memset(fr, 0, sizeof fr);
    else {
      float q[3];
      p1a_rand_unit(fr);
      for (int k = 0; k < 3; k++) fr[3 + k] = c[k] + frand(-2, 2);
      p1a_rand_unit(q);
      p1p_cross(fr, q, fr + 6);
      p1p_norm(fr + 6);
      p1p_cross(fr, fr + 6, fr + 9);
    }
    if (i % 11 == 5) fr[1] = bitsf(0x7FC00000u); /* NaN normal: ucomiss/jne takes the "zero" branch */
    int feat;
    if (type == 0)
      feat = slot == P1P_PolyA ? 0x40 | (int)frand(0, 2.999f) << 1 | (int)frand(0, 1.999f)
                               : 0x20 | (int)frand(0, 2.999f) << 2 | (int)frand(0, 3.999f);
    else {
      float r = frand(0, 1);
      feat = r < 0.4f ? 0x41 : r < 0.8f ? 0x42 : r < 0.9f ? 0x40 : (int)frand(0, 255.999f);
    }
    printf(" I");
    p1p_print_frame(gt);
    p1a_out_w(0, fr, 12);
    printf(" %d", feat);
    void *pts = 0, *ida = 0, *idb = 0;
    int n = ((p1p_fn58)VT(g)[slot])(g, fr, 0, feat, gt, &pts, &ida, &idb);
    printf(" O %d", n);
    p1a_out_w(0, fr, 12);
    p1a_out_w(0, pts, 2 * (n + 1));
    printf(" %d", ida == idb);
    for (int k = 0; k < n; k++) printf(" %d", ((int *)ida)[k]);
    for (int k = 0; k < n; k++) printf(" %d", ((int *)idb)[k]);
  } else if (!strcmp(probe, "reg54")) {
    static __declspec(align(16)) unsigned char contacts[136 * 8];
    __declspec(align(16)) unsigned char params[160], inters[96], primb[64];
    memset(gt, 0, sizeof gt);
    memset(gt2, 0, sizeof gt2);
    p1p_frame(gt, mode, ic);
    p1p_prep(g, gt);
    memset(contacts, 0xCD, sizeof contacts);
    memset(params, 0, sizeof params);
    int cnt = (int)frand(0, 2.999f), nmax = (int)frand(1, 4.999f), b98 = frand(0, 1) < 0.2f;
    params[98] = (unsigned char)b98;
    GT_P(gt, 216) = params;
    GT_P(gt, 368) = contacts;
    GT_P(gt, 376) = &cnt;
    GT_I(gt, 384) = nmax;
    GT_I(gt, 40) = 0;
    int hasb = (i % 5) != 0, tb = frand(0, 1) < 0.5f ? 1 : (int)frand(0, 6.999f);
    GT_I(gt2, 280) = tb;
    for (int k = 0; k < 16; k++) ((uint32_t *)primb)[k] = p1p_rand_word();
    for (int k = 0; k < 22; k++) ((float *)inters)[k] = frand(-5, 5);
    for (int k = 36; k < 40; k++) inters[k] = (unsigned char)frand(0, 255.999f);
    for (int k = 44; k < 56; k++) inters[k] = (unsigned char)frand(0, 255.999f);
    printf(" I");
    p1p_print_frame(gt);
    printf(" %d %d %d %d %d", cnt, nmax, b98, hasb, tb);
    p1a_out_w(0, primb + 48, 1);
    p1a_out_w(0, inters, 14);
    int ret = ((p1p_fn54)VT(g)[P1P_Reg])(g, GT_P(gt, 288), primb, gt, hasb ? gt2 : 0, inters);
    unsigned char *c = contacts + 136 * (cnt - 1);
    printf(" O %d %d %d", ret, cnt, GT_I(gt, 40));
    p1a_out_w(0, c, 21);
    printf(" %d %d %d", *(void **)(c + 88) == c + 8, GT_I(c, 104), *(void **)(c + 128) == 0);
    p1a_out_w(0, c + 112, 3);
    p1a_out_w(0, c + 96, 2);
    p1a_out_w(0, c + 124, 1);
  } else if (!strcmp(probe, "unproj49")) {
    float c[3], r, rsep, wc[3];
    __declspec(align(16)) unsigned char res[48], res2[48], up[64];
    __declspec(align(16)) float sph[4];
    float idg[32];
    memset(idg, 0, sizeof idg);
    p1a_identity(idg + 3);
    idg[12] = 1;
    p1a_world_center(type, prim, idg, wc);
    float rad = type == 0 ? sqrtf(prim[13] * prim[13] + prim[14] * prim[14] + prim[15] * prim[15])
                          : type == 4 ? prim[3] : sqrtf(prim[6] * prim[6] + prim[7] * prim[7]) + (type == 6 ? prim[6] : 0);
    if (frand(0, 1) < 0.15f && type != 3) {
      for (int k = 0; k < 3; k++) c[k] = wc[k];
    } else if (frand(0, 1) < 0.5f && (type == 5 || type == 6)) {
      float q[3], e[3], h = frand(-1.3f, 1.3f) * prim[7];
      p1a_rand_unit(q);
      p1p_cross(prim + 3, q, e);
      p1p_norm(e);
      if (frand(0, 1) < 0.3f) h = (frand(0, 1) < 0.5f ? 1 : -1) * prim[7];
      float rr = frand(0, 1.5f) * prim[6];
      for (int k = 0; k < 3; k++) c[k] = prim[k] + prim[3 + k] * h + e[k] * rr;
    } else {
      for (int k = 0; k < 3; k++) c[k] = wc[k] + frand(-1.3f, 1.3f) * rad;
    }
    r = frand(0.01f, 2);
    rsep = frand(0, 1) < 0.2f ? r : r * frand(0.3f, 3);
    for (int k = 0; k < 12; k++) ((uint32_t *)res)[k] = p1p_rand_word();
    memcpy(res2, res, 48);
    printf(" I");
    p1a_out_w(0, c, 3);
    p1a_out_w(0, &r, 1);
    p1a_out_w(0, &rsep, 1);
    p1a_out_w(0, res, 12);
    /* box / cylinder: the pair functions the slot forwards into, called directly with the same
       arguments (OVERLAP[box][sph] 0x35196CE0, UNPROJECT[0][box][sph] 0x35185B80,
       UNPROJECT[0][cyl][sph] 0x35187780) so the Rust side can replay them */
    if (type == 0 || type == 5) {
      memcpy(sph, c, 12);
      sph[3] = type == 0 ? rsep : r;
      int ov = type == 0 ? ((p1p_fnov)VA(g_phys, 0x35196CE0ULL))((char *)g + 48, sph, 0) : 1;
      int u = 0;
      memset(up, 0, sizeof up);
      *(float *)(up + 44) = 0.1f;
      if (ov) u = ((p1p_fnun)VA(g_phys, type == 0 ? 0x35185B80ULL : 0x35187780ULL))(up, (char *)g + 48, -1, sph, -1, res2, 0);
      printf(" C %d %d", ov, u);
      p1a_out_w(0, res2, 12);
      p1a_out_w(0, up + 4, 3);
    }
    int ret = ((p1p_fn49)VT(g)[P1P_Unproj])(g, c, r, rsep, res);
    printf(" O %d", ret);
    p1a_out_w(0, res, 12);
  } else {
    die("p1a protocol: unknown probe", probe);
  }
}

static void cmd_gproto(const Toks *t) {
  const char *probe = need(t, "probe");
  int type = p1a_type_id(need(t, "type"));
  int n = atoi(get(t, "n", "100"));
  int mode = atoi(get(t, "mode", "0"));
  g_rng = strtoull(get(t, "seed", "1"), 0, 0);
  for (int i = 0; i < n; i++) {
    __declspec(align(16)) float prim[32];
    p1a_rand_prim(type, prim);
    if (mode == 2 && type == 0) prim[13 + (int)frand(0, 2.999f)] *= 0.05f; /* thin boxes */
    void *g = p1a_create(type, prim);
    printf("gp %s %d", probe, type);
    p1a_out_w("P", prim, p1a_prim_words(type));
    p1p_case(probe, type, mode, i, g, prim);
    printf("\n");
    p1a_release(g);
  }
}

static int p1a_cmd_protocol(const char *c, const Toks *t) {
  if (!strcmp(c, "gproto")) cmd_gproto(t);
  else return 0;
  return 1;
}
