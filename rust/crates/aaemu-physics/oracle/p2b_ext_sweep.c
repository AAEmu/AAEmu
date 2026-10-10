/* P2b probe extension for the "sweep" work group: primitive geometries through the real geometry
 * objects (CGeomManager::CreatePrimitive), PreparePrimitive (geometry slot 48) and the primitive
 * Intersect (slot 10, 0x35151F20) with sweep tests.
 *
 *   PREP <type> <prim words> G <22 gwd words>
 *     -> prep <ret type> <prim words of the returned type> BB <16 bbox words> MVD <minVtxDist>
 *   SWEEP <tA> <primA words> <tB> <primB words> GA <22 words> GB <22 words> IP <28 words>
 *     -> sweep <ret> GA <22 words after> BB <16 words: bbox of B> MVD <A +16> <B +16>
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

static int p2b_ext_sweep(char *line) {
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
    int ret = ((p2b_isect_fn)VT(a)[10])(a, b, (float *)ga, (float *)gb, ip, &pc);
    printf("sweep %08x GA", (uint32_t)ret);
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
