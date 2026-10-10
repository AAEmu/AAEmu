/* P2b probe extension for the "sweep" work group: primitive geometries through the real geometry
 * objects (CGeomManager::CreatePrimitive), PreparePrimitive (geometry slot 48) and the primitive
 * Intersect (slot 10, 0x35151F20) with sweep tests.
 *
 *   PREP <type> <prim words> G <22 gwd words>
 *     -> prep <ret type> <prim words of the returned type> BB <16 bbox words> MVD <minVtxDist>
 *   SWEEP <tA> <primA words> <tB> <primB words> GA <22 words> GB <22 words> IP <28 words>
 *     -> sweep <ret> OV <overlap pre-check passed 0|1> GA <22 words after> BB <16 words: bbox of B> MVD <A +16> <B +16>
 *        [C <contact words> B <nborder> <border pts> CEN <3> F <byte +124> AREA <npt> <nmax> <minedge> <n3> <pts>|NOAREA]
 *
 * gwd: offset xyz, R (9, row-major), scale, v xyz, w xyz, centerOfMass xyz (88 bytes).
 * IP: the first 112 bytes of intersection_params (the rest is zero; Intersect sets +128 itself).
 * Contact words: t lo hi, pt, n, dir, iUnprojMode, vel, id[2], iPrim[2], iFeature[2], iNode[2].
 */
typedef void *(*p2b_create_prim_fn)(void *gm, int type, void *prim);
typedef int (*p2b_prep_fn)(void *geom, float *gwd, float **out, int caller);
typedef int (*p2b_isect_fn)(void *geom, void *other, float *gwd1, float *gwd2, void *ip, void **pcontacts);
typedef int (*p2b_bbox_fn)(void *geom, void *box);
static void p2b_print_bbox(void *g) {
  __declspec(align(16)) uint32_t bb[32];
  memset(bb, 0, sizeof bb);
  ((p2b_bbox_fn)VT(g)[6])(g, bb);
  p2b_print_words(bb, 16);
}

static int g_p2b_world_ready;
static void p2b_world(void) {
  if (g_p2b_world_ready) return;
  g_world = ((void *(*)(void *))GetProcAddress(g_phys, "CreatePhysicalWorld"))(NULL);
  if (!g_world) die("CreatePhysicalWorld failed", "");
  install_fake_env(g_phys);
  g_gm = ((fn_p)VT(g_world)[W_GetGeomManager])(g_world);
  g_p2b_world_ready = 1;
}
static void *p2b_geom(int type, const uint32_t *prim) {
  __declspec(align(16)) uint32_t buf[64];
  memset(buf, 0, sizeof buf);
  int n = p2b_prim_words(type);
  memcpy(buf, prim, 4 * n);
  void *g = ((p2b_create_prim_fn)VT(g_gm)[G_CreatePrimitive])(g_gm, type, buf);
  if (!g) die("p2b: CreatePrimitive failed", "");
  return g;
}


/* The OVERLAP pre-check of the sweep path of 0x35151F20, replicated (A's swept BV: slot 46 then
   BV slot 8; B's BV: slot 46 then BV slot 9; OVERLAP[tA][tB] with the per-thread scratch). Run
   after Intersect so its per-thread pools are not disturbed. */
typedef void *(*p2b_bvtree_fn)(void *geom);
typedef void (*p2b_bv_swept_fn)(void *bv, float *R, float *off, float scale, void **out, float *dir, float len, int z, int tid);
typedef void (*p2b_bv_fn)(void *bv, float *R, float *off, float scale, void **out, int z, int tid);
typedef int (*p2b_overlap_fn)(void *a, void *b, void *scratch);
static int p2b_sweep_overlap(void *a, void *b, float *ga, float *gb, const uint32_t *ip) {
  DWORD tls = *(DWORD *)VA(g_phys, 0x3520F3A4ULL);
  void *tv = TlsGetValue(tls);
  int tid = tv ? *(int *)tv : 4;
  float vx = ga[13], vy = ga[14], vz = ga[15];
  float len = sqrtf((vx * vx + vy * vy) + vz * vz);
  float inv = 1.0f / len;
  float ti;
  memcpy(&ti, &ip[7], 4);
  float L = len * ti;
  float dir[3] = {vx * inv, inv * vy, inv * vz};
  void *bva = ((p2b_bvtree_fn)VT(a)[46])(a);
  void *oa = 0, *ob = 0;
  ((p2b_bv_swept_fn)VT(bva)[8])(bva, ga + 3, ga, ga[12], &oa, dir, L, 0, tid);
  void *bvb = ((p2b_bvtree_fn)VT(b)[46])(b);
  ((p2b_bv_fn)VT(bvb)[9])(bvb, gb + 3, gb, gb[12], &ob, 0, tid);
  int ta = *(int *)oa, tb = *(int *)ob;
  p2b_overlap_fn f = *(p2b_overlap_fn *)VA(g_phys, 0x3520A3E0ULL + 8ULL * (ta * 8 + tb));
  return f((char *)oa + 8, (char *)ob + 8, VA(g_phys, 0x35290EE8ULL + 237496ULL * (uint64_t)tid));
}


/* REG <tA> <primA> <tB> <primB> SA <13: v w com rc mvd> SB <13> PI <22 words prim_inters>
       IP <28> AREA <0|1>
   -> reg <ncontacts> STOP <flag> PI <n xyz after> C <22 contact words> NB <+104> SELF <+88 == &pt>
      [AREA <npt> <nmax> <minedge> <n3> <pts…>|NOAREA]
   Calls CGeometry::RegisterIntersection (slot 54, 0x3514F3F0) with hand-built
   geometry_under_test records (+40 stop, +164 v, +176 w, +188 com, +200 rot centre, +216 ip,
   +280 type, +360 minVtxDist, +368 contacts, +376 &count, +384 nMax, +388 thread). The first
   contact slot is pre-filled with 0x11111111 words. */
typedef int (*p2b_reg_fn)(void *self, void *pa, void *pb, void *gta, void *gtb, void *inters);
static void p2b_gtest_side(char **cur, unsigned char *gt) {
  uint32_t w[13];
  p2b_words(cur, w, 13);
  memcpy(gt + 164, w, 48);
  memcpy(gt + 360, &w[12], 4);
}
static int p2b_ext_reg(char *line) {
  char *cur = line;
  char *cmd = p2b_tok(&cur);
  if (!cmd || strcmp(cmd, "REG")) return 0;
  p2b_world();
  __declspec(align(16)) uint32_t pa[16], pb[16], pi[32], ip[64];
  static __declspec(align(16)) unsigned char gta[512], gtb[512], contacts[136 * 4];
  static __declspec(align(16)) float apts[256 * 3];
  static int ap0[256], ap1[256], af0[256], af1[256];
  static __declspec(align(16)) unsigned char area[80];
  memset(gta, 0, sizeof gta); memset(gtb, 0, sizeof gtb); memset(ip, 0, sizeof ip);
  memset(pi, 0, sizeof pi);
  int ta = (int)p2b_word(&cur);
  p2b_words(&cur, pa, p2b_prim_words(ta));
  int tb = (int)p2b_word(&cur);
  p2b_words(&cur, pb, p2b_prim_words(tb));
  p2b_expect(&cur, "SA"); p2b_gtest_side(&cur, gta);
  p2b_expect(&cur, "SB"); p2b_gtest_side(&cur, gtb);
  p2b_expect(&cur, "PI"); p2b_words(&cur, pi, 22);
  p2b_expect(&cur, "IP"); p2b_words(&cur, ip, 28);
  p2b_expect(&cur, "AREA");
  int use_area = (int)p2b_word(&cur);
  memset(contacts, 0x11, sizeof contacts);
  int count = 0;
  for (int i = 0; i < 4; i++) *(void **)(contacts + 136 * i + 128) = 0;
  if (use_area) {
    memset(area, 0, sizeof area);
    *(int *)(area + 8) = 256;
    *(float *)(area + 12) = 0.0f;
    *(int **)(area + 16) = ap0; *(int **)(area + 24) = ap1;
    *(int **)(area + 32) = af0; *(int **)(area + 40) = af1;
    *(float **)(area + 48) = apts;
    *(void **)(contacts + 128) = area;
  }
  void *ipp = ip;
  *(int *)(gta + 280) = ta; *(int *)(gtb + 280) = tb;
  memcpy(gta + 216, &ipp, 8); memcpy(gtb + 216, &ipp, 8);
  void *cp = contacts, *cnt = &count;
  memcpy(gta + 368, &cp, 8); memcpy(gta + 376, &cnt, 8);
  *(int *)(gta + 384) = 4; *(int *)(gta + 388) = 4;
  ((p2b_reg_fn)VA(g_phys, 0x3514F3F0ULL))(0, pa, pb, gta, gtb, pi);
  printf("reg %08x STOP %08x PI", (uint32_t)count, *(uint32_t *)(gta + 40));
  p2b_print_words(pi + 6, 3);
  printf(" C");
  p2b_print_words((uint32_t *)contacts, 22);
  printf(" NB %08x SELF %d", *(uint32_t *)(contacts + 104), *(void **)(contacts + 88) == (void *)(contacts + 8));
  unsigned char *ar = *(unsigned char **)(contacts + 128);
  if (ar) {
    int npt = *(int *)(ar + 4);
    printf(" AREA %08x %08x %08x", (uint32_t)npt, *(uint32_t *)(ar + 8), *(uint32_t *)(ar + 12));
    p2b_print_words((uint32_t *)(ar + 56), 3);
    for (int i = 0; i < npt && i < 256; i++) p2b_print_words((uint32_t *)(apts + 3 * i), 3);
  } else {
    printf(" NOAREA");
  }
  printf("\n");
  return 1;
}

static int p2b_ext_sweep(char *line) {
  if (p2b_ext_reg(line)) return 1;
  char *cur = line;
  char *cmd = p2b_tok(&cur);
  if (!cmd) return 0;
  if (!strcmp(cmd, "PREP")) {
    p2b_world();
    uint32_t prim[16], gwd[24];
    int t = (int)p2b_word(&cur);
    int n = p2b_prim_words(t);
    if (n < 0) die("p2b: bad type", "");
    p2b_words(&cur, prim, n);
    p2b_expect(&cur, "G");
    p2b_words(&cur, gwd, 22);
    void *g = p2b_geom(t, prim);
    float *out = 0;
    int rt = ((p2b_prep_fn)VT(g)[48])(g, (float *)gwd, &out, 0);
    printf("prep %08x", (uint32_t)rt);
    int m = p2b_prim_words(rt);
    if (m < 0 || !out) die("p2b: bad prepared type", "");
    p2b_print_words((uint32_t *)out, m);
    printf(" BB");
    p2b_print_bbox(g);
    printf(" MVD %08x\n", *(uint32_t *)((char *)g + 16));
    return 1;
  }
  if (!strcmp(cmd, "SWEEP")) {
    p2b_world();
    uint32_t pa[16], pb[16];
    __declspec(align(16)) uint32_t ga[24], gb[24], ip[64];
    memset(ip, 0, sizeof ip);
    int ta = (int)p2b_word(&cur);
    int na = p2b_prim_words(ta);
    if (na < 0) die("p2b: bad type", "");
    p2b_words(&cur, pa, na);
    int tb = (int)p2b_word(&cur);
    int nb = p2b_prim_words(tb);
    if (nb < 0) die("p2b: bad type", "");
    p2b_words(&cur, pb, nb);
    p2b_expect(&cur, "GA");
    p2b_words(&cur, ga, 22);
    p2b_expect(&cur, "GB");
    p2b_words(&cur, gb, 22);
    p2b_expect(&cur, "IP");
    p2b_words(&cur, ip, 28);
    void *a = p2b_geom(ta, pa), *b = p2b_geom(tb, pb);
    void *pc = 0;
    __declspec(align(16)) uint32_t ga0[24];
    memcpy(ga0, ga, sizeof ga0);
    int ret = ((p2b_isect_fn)VT(a)[10])(a, b, (float *)ga, (float *)gb, ip, &pc);
    int ov = p2b_sweep_overlap(a, b, (float *)ga0, (float *)gb, ip);
    printf("sweep %08x OV %x GA", (uint32_t)ret, ov ? 1 : 0);
    p2b_print_words(ga, 22);
    printf(" BB");
    p2b_print_bbox(b);
    printf(" MVD %08x %08x", *(uint32_t *)((char *)a + 16), *(uint32_t *)((char *)b + 16));
    if (ret > 0 && pc) {
      unsigned char *c = (unsigned char *)pc;
      printf(" C");
      p2b_print_words((uint32_t *)c, 22);
      int nbp = *(int *)(c + 104);
      float *bp = *(float **)(c + 88);
      printf(" B %08x", (uint32_t)nbp);
      for (int i = 0; i < nbp && i < 256; i++) p2b_print_words((uint32_t *)(bp + 3 * i), 3);
      printf(" CEN");
      p2b_print_words((uint32_t *)(c + 112), 3);
      printf(" F %02x", c[124]);
      unsigned char *ar = *(unsigned char **)(c + 128);
      if (ar) {
        int npt = *(int *)(ar + 4);
        printf(" AREA %08x %08x %08x", (uint32_t)npt, *(uint32_t *)(ar + 8), *(uint32_t *)(ar + 12));
        p2b_print_words((uint32_t *)(ar + 56), 3);
        float *apt = *(float **)(ar + 48);
        int *p0 = *(int **)(ar + 16), *p1 = *(int **)(ar + 24), *f0 = *(int **)(ar + 32), *f1 = *(int **)(ar + 40);
        for (int i = 0; i < npt && i < 256; i++) {
          p2b_print_words((uint32_t *)(apt + 3 * i), 3);
          printf(" %08x %08x %08x %08x", (uint32_t)p0[i], (uint32_t)p1[i], (uint32_t)f0[i], (uint32_t)f1[i]);
        }
      } else {
        printf(" NOAREA");
      }
    }
    printf("\n");
    return 1;
  }
  return 0;
}
