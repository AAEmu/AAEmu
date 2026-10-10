/* P3 areas + P4a water/air probes (package P3-areas), #included by physoracle.c before run_line.
 *
 * Commands (all floats printed as raw bits):
 *   area name= kind=spline pts=x,y,z;x,y,z;... r= [pos=] [q=] [scale=1]     world slot 81
 *   area name= kind=geom geom=G [pos=] [q=] [scale=1]                       world slot 82
 *   rmarea target=A                                                          world slot 84
 *   params target=A type=18 [fields]      area params with the "all unused" template
 *   checkareas tag= pt= [ent=E] [r=0] [imedium=-1] [nmax=4] [vel=0,0,0] [caller=4] [nareas=0]
 *        world slot 87 -> "ca TAG ret G gx gy gz B <nmax*20 words> A <names>"
 *   gather tag= ent= dt= [nmax=4]           sub_350C4880 -> "ga TAG ret B <words> S <state>"
 *   abuoy tag= ent= dt= [g=0,0,-9.81] [n=]  ApplyBuoyancy 0x350C0E50 on the last ca/ga buffer
 *   aair tag= ent= dt=                      ApplyAirResistance 0x350C2790
 *   estate tag= ent=                        print the entity state
 *   poke target= off= f=|i=|v=|q=           raw write into an entity/area object
 *   areacb mode=N [prio=]                   register an immediate event-11 client (see p3_areacb)
 *   areanext [after=A] / areavalid target=A world slots 85 / 86
 */

#define P3_BUF_RECS 8
static unsigned char g_p3_buoy[80 * P3_BUF_RECS];
static int g_p3_nbuoy;
static int g_p3_cbmode;

typedef void *(*p3_fn_spline)(void *, float *, int, float, float *, float *, float);
typedef void *(*p3_fn_garea)(void *, void *, float *, float *, float);
typedef void (*p3_fn_rm)(void *, void *);
typedef int (*p3_fn_check)(void *, float *, float *, void *, int, int, float *, void *, float, int, void **, int);
typedef int (*p3_fn_gather)(void *, float, void *, int);
typedef void (*p3_fn_buoy)(void *, float, float *, void *, int);
typedef void (*p3_fn_air)(void *, float);
typedef int (*p3_fn_evc)(const void *);
typedef void (*p3_fn_addevc)(void *, int, p3_fn_evc, int, float);
typedef void *(*p3_fn_next)(void *, void *);
typedef char (*p3_fn_valid)(void *, void *);

static void p3_fill_buf(void) {
  for (int r = 0; r < P3_BUF_RECS; r++) {
    unsigned char *b = g_p3_buoy + 80 * r;
    for (int o = 0; o < 80; o += 4) *(uint32_t *)(b + o) = 0x7F7F7F7Fu;
    *(uint32_t *)b = 12;
  }
}

static void p3_print_buf(int n) {
  printf(" B");
  for (int i = 0; i < 20 * n; i++) printf(" %08x", ((uint32_t *)g_p3_buoy)[i]);
}

static void p3_print_state(void *e) {
  unsigned char *p = (unsigned char *)e;
  printf(" S");
  int offs[] = {772, 784, 796, 808, 956, 968};
  for (int k = 0; k < 6; k++) out_f((float *)(p + offs[k]), 3);
  printf(" %08x %08x %08x %08x %08x", *(uint32_t *)(p + 692), *(uint32_t *)(p + 1136), *(uint32_t *)(p + 504),
         *(uint32_t *)(p + 608), *(uint32_t *)(p + 252));
  printf(" %d %d %08x", (int)(signed char)p[71], (int)p[508], *(uint32_t *)(p + 1104));
}

/* event-11 client: mode 1 = set gravity (0,0,-3) and a water plane at z=2 (density 500);
 * mode 2 = only bump the flow; returns 1 */
static int p3_areacb(const void *ev) {
  unsigned char *b = (unsigned char *)ev;
  if (g_p3_cbmode == 1) {
    float g[3] = {0, 0, -3};
    memcpy(b + 156, g, 12);
    *(float *)(b + 76 + 4) = 500.0f;
    float n[3] = {0, 0, 1}, o[3] = {0, 0, 2};
    memcpy(b + 76 + 40, n, 12);
    memcpy(b + 76 + 52, o, 12);
  } else if (g_p3_cbmode == 2) {
    *(float *)(b + 76 + 24) += 1.0f;
  }
  printf("cb11 pt");
  out_f((float *)(b + 40), 3);
  printf(" c");
  out_f((float *)(b + 52), 3);
  printf(" d");
  out_f((float *)(b + 64), 3);
  printf(" e %s\n", name_of(*(void **)(b + 168)));
  return 1;
}

static int p3_tmpl18(unsigned char *b) {
  memset(b, 0, 512);
  SETI(b, 0, 18);
  fill_unused_f(b, 4, 32);
  SETI(b, 32, UNUSED_I);
  SETI(b, 36, UNUSED_I);
  fill_unused_f(b, 40, 44);
  SETP(b, 48, -1);
  fill_unused_f(b, 56, 72);
  return 72;
}

static int p3_command(const char *c, const Toks *t) {
  if (!strcmp(c, "area")) {
    const char *kind = get(t, "kind", "global");
    if (!strcmp(kind, "global")) return 0;
    float pos[3] = {0, 0, 0}, q[4] = {0, 0, 0, 1};
    parse_fv(get(t, "pos", "0,0,0"), pos, 3);
    parse_fv(get(t, "q", "0,0,0,1"), q, 4);
    float scale = parse_f(get(t, "scale", "1"));
    void *a = 0;
    if (!strcmp(kind, "spline")) {
      static float pts[3 * 256];
      int n = 0;
      char buf[4096];
      strncpy(buf, need(t, "pts"), sizeof buf - 1);
      buf[sizeof buf - 1] = 0;
      for (char *s = strtok(buf, ";"); s && n < 256; s = strtok(0, ";")) {
        float v[3] = {0, 0, 0};
        char one[256];
        strncpy(one, s, 255);
        one[255] = 0;
        /* parse_fv uses strtok internally: parse by hand */
        char *e1 = one;
        for (int k = 0; k < 3; k++) {
          char *comma = strchr(e1, ',');
          if (comma) *comma = 0;
          v[k] = parse_f(e1);
          if (!comma) break;
          e1 = comma + 1;
        }
        memcpy(pts + 3 * n, v, 12);
        n++;
      }
      a = ((p3_fn_spline)VT(g_world)[81])(g_world, pts, n, parse_f(need(t, "r")), pos, q, scale);
    } else if (!strcmp(kind, "geom")) {
      void *pg = find_obj(need(t, "geom"), 0);
      if (!pg) die("unknown geom", need(t, "geom"));
      a = ((p3_fn_garea)VT(g_world)[82])(g_world, *(void **)pg, pos, q, scale);
    } else die("unknown area kind", kind);
    if (!a) die("area creation failed", need(t, "name"));
    add_obj(need(t, "name"), a, 2);
    printf("area %s bbox", need(t, "name"));
    out_f((float *)((char *)a + 8), 6);
    printf("\n");
    return 1;
  }
  if (!strcmp(c, "rmarea")) {
    ((p3_fn_rm)VT(g_world)[84])(g_world, find_target(need(t, "target")));
    return 1;
  }
  if (!strcmp(c, "params") && atoi(get(t, "type", "0")) == 18) {
    void *e = find_target(need(t, "target"));
    __declspec(align(16)) unsigned char b[1024];
    p3_tmpl18(b);
    apply_fields(t, b, 512);
    int r = ((fn_setp)VT(e)[E_SetParams])(e, b, 1);
    printf("ret params %s 18 %d\n", need(t, "target"), r);
    return 1;
  }
  if (!strcmp(c, "checkareas")) {
    float pt[3] = {0, 0, 0}, vel[3] = {0, 0, 0}, g[3] = {1, 2, 3};
    parse_fv(need(t, "pt"), pt, 3);
    parse_fv(get(t, "vel", "0,0,0"), vel, 3);
    const char *en = get(t, "ent", 0);
    void *ent = en ? find_target(en) : 0;
    int nmax = atoi(get(t, "nmax", "4"));
    if (nmax > P3_BUF_RECS) nmax = P3_BUF_RECS;
    int nareas = atoi(get(t, "nareas", "0"));
    void *areas[16];
    for (int i = 0; i < 16; i++) areas[i] = 0;
    if (nareas > 16) nareas = 16;
    p3_fill_buf();
    int r = ((p3_fn_check)VT(g_world)[87])(g_world, pt, g, g_p3_buoy, nmax, (int)parse_u(get(t, "imedium", "-1")), vel,
                                           ent, parse_f(get(t, "r", "0")), atoi(get(t, "caller", "4")),
                                           nareas ? areas : 0, nareas);
    g_p3_nbuoy = r;
    printf("ca %s %d G", get(t, "tag", "-"), r);
    out_f(g, 3);
    p3_print_buf(nmax);
    printf(" A");
    for (int i = 0; i < nareas; i++) printf(" %s", name_of(areas[i]));
    printf("\n");
    return 1;
  }
  if (!strcmp(c, "gather")) {
    void *e = find_target(need(t, "ent"));
    int nmax = atoi(get(t, "nmax", "4"));
    if (nmax > P3_BUF_RECS) nmax = P3_BUF_RECS;
    p3_fill_buf();
    int r = ((p3_fn_gather)VA(g_phys, 0x350C4880ULL))(e, parse_f(need(t, "dt")), g_p3_buoy, nmax);
    g_p3_nbuoy = r;
    printf("ga %s %d", get(t, "tag", "-"), r);
    p3_print_buf(nmax);
    p3_print_state(e);
    printf("\n");
    return 1;
  }
  if (!strcmp(c, "abuoy")) {
    void *e = find_target(need(t, "ent"));
    float g[3] = {0, 0, -9.81f};
    parse_fv(get(t, "g", "0,0,-9.81"), g, 3);
    int n = atoi(get(t, "n", "-1"));
    if (n < 0) n = g_p3_nbuoy;
    if (n < 0) n = 0;
    ((p3_fn_buoy)VA(g_phys, 0x350C0E50ULL))(e, parse_f(need(t, "dt")), g, g_p3_buoy, n);
    printf("ab %s", get(t, "tag", "-"));
    p3_print_state(e);
    printf("\n");
    return 1;
  }
  if (!strcmp(c, "aair")) {
    void *e = find_target(need(t, "ent"));
    ((p3_fn_air)VA(g_phys, 0x350C2790ULL))(e, parse_f(need(t, "dt")));
    printf("aa %s", get(t, "tag", "-"));
    p3_print_state(e);
    printf("\n");
    return 1;
  }
  if (!strcmp(c, "estate")) {
    void *e = find_target(need(t, "ent"));
    printf("es %s", get(t, "tag", "-"));
    p3_print_state(e);
    printf("\n");
    return 1;
  }
  if (!strcmp(c, "poke")) {
    unsigned char *p = (unsigned char *)find_target(need(t, "target"));
    int off = atoi(need(t, "off"));
    const char *v;
    if ((v = get(t, "f", 0))) *(float *)(p + off) = parse_f(v);
    else if ((v = get(t, "i", 0))) *(uint32_t *)(p + off) = parse_u(v);
    else if ((v = get(t, "v", 0))) parse_fv(v, (float *)(p + off), 3);
    else if ((v = get(t, "q", 0))) parse_fv(v, (float *)(p + off), 4);
    else die("poke needs f/i/v/q", 0);
    return 1;
  }
  if (!strcmp(c, "areacb")) {
    g_p3_cbmode = atoi(get(t, "mode", "0"));
    ((p3_fn_addevc)VT(g_world)[75])(g_world, 11, p3_areacb, 0, parse_f(get(t, "prio", "0")));
    return 1;
  }
  if (!strcmp(c, "areanext")) {
    const char *af = get(t, "after", 0);
    void *a = ((p3_fn_next)VT(g_world)[85])(g_world, af ? find_target(af) : 0);
    printf("areanext %s\n", name_of(a));
    return 1;
  }
  if (!strcmp(c, "areavalid")) {
    char r = ((p3_fn_valid)VT(g_world)[86])(g_world, find_target(need(t, "target")));
    printf("areavalid %s %d\n", need(t, "target"), (int)r);
    return 1;
  }
  return 0;
}
