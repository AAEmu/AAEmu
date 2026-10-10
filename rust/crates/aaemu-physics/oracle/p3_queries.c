/* P3 query commands (additive; included from physoracle.c before run_line).
 *
 *   rayx name= org= dir= [types=0x13F] [flags=0] [max=1] [caller=4] [skip=a,b,-] [hitlast=NAME] [keep=NAME]
 *        RayWorldIntersection with skip entities, a persistent hit-last cache (printed as
 *        "hl NAME e ENT ip IPART nd INODE") and persistent hit buffers (keep=: the buffer's old
 *        contents are what the query sees).
 *   pwi  name= geom=G [at=x,y,z] [sweep=x,y,z] [types=0x13F] [all=0] [any=0xFFFFFFFF] [stop=0|1]
 *        [skip=a,b] [only=a,b]   PrimitiveWorldIntersection (slot 65); stop= passes intersection
 *        params with byte +98 = stop; only= passes the list as entities to test (nSkip < 0).
 *        Prints "pwi NAME d BITS" and, when d != 0, the first contact "c t .. p .. n .. id a b pr x y".
 *   rte  name= ent= org= dir= [any=0xFFFFFFFF] [backfaces=0]          RayTraceEntity (slot 61)
 *   cewp name= ent= geom=G [at=] [sweep=] [any=] [inside=0]           CollideEntityWithPrimitive (slot 60)
 *   beam name= ent= org= dir= r= [any=]                                CollideEntityWithBeam (slot 59)
 *   surface idx= flags=          write world surface flags (+18792 + 4*idx)
 *   fdata target= v=  [i=]       set an entity's foreign data (+32) / iForeignData (+40)
 */

typedef float (*fn_pwi)(void *, void *, const char *);
typedef int (*fn_rte)(void *, void *, float *, float *, void *, void *, int, char);
typedef int (*fn_cewp)(void *, void *, int, void *, float *, void *, void *, int, char);
typedef int (*fn_beam)(void *, void *, float *, float *, float, void *, int);
typedef int (*fn_gprim)(void *, int, void *);
typedef int (*fn_gtype)(void *);

#define P3_MAXBUF 32
static struct { char name[32]; unsigned char hits[80 * 16]; } g_p3buf[P3_MAXBUF];
static int g_p3nbuf;
static struct { char name[32]; unsigned char hl[16]; } g_p3hl[P3_MAXBUF];
static int g_p3nhl;

static unsigned char *p3_buf(const char *name) {
  for (int i = 0; i < g_p3nbuf; i++)
    if (!strcmp(g_p3buf[i].name, name)) return g_p3buf[i].hits;
  if (g_p3nbuf >= P3_MAXBUF) die("too many hit buffers", name);
  strncpy(g_p3buf[g_p3nbuf].name, name, 31);
  memset(g_p3buf[g_p3nbuf].hits, 0, sizeof g_p3buf[0].hits);
  return g_p3buf[g_p3nbuf++].hits;
}
static unsigned char *p3_hl(const char *name) {
  for (int i = 0; i < g_p3nhl; i++)
    if (!strcmp(g_p3hl[i].name, name)) return g_p3hl[i].hl;
  if (g_p3nhl >= P3_MAXBUF) die("too many hit-last caches", name);
  strncpy(g_p3hl[g_p3nhl].name, name, 31);
  memset(g_p3hl[g_p3nhl].hl, 0, 16);
  return g_p3hl[g_p3nhl++].hl;
}

static int p3_skip(const char *s, void **out, int maxn) {
  int n = 0;
  char buf[512];
  if (!s) return 0;
  strncpy(buf, s, sizeof buf - 1);
  buf[sizeof buf - 1] = 0;
  for (char *t = strtok(buf, ","); t && n < maxn; t = strtok(0, ",")) out[n++] = !strcmp(t, "-") ? 0 : find_target(t);
  return n;
}

static void p3_print_hit(int i, unsigned char *h) {
  printf("h %d d %08x e %s ip %d pid %d s %d p", i, fbits(*(float *)h), name_of(*(void **)(h + 8)), *(int *)(h + 16),
         *(int *)(h + 20), *(int *)(h + 28));
  out_f((float *)(h + 36), 3);
  printf(" n");
  out_f((float *)(h + 48), 3);
  printf("\n");
}

/* the primitive of a registered geometry (phys_geometry +0 = IGeometry*), centre moved to at= */
static int p3_prim(const Toks *t, unsigned char *prim) {
  void *pg = find_obj(need(t, "geom"), 0);
  if (!pg) die("unknown geom", need(t, "geom"));
  void *g = *(void **)pg;
  int type = ((fn_gtype)VT(g)[1])(g);
  memset(prim, 0, 256);
  ((fn_gprim)VT(g)[17])(g, 0, prim);
  const char *at = get(t, "at", 0);
  if (at) parse_fv(at, (float *)(prim + (type == 0 ? 40 : 0)), 3);
  return type;
}

/* intersection_params with the inline defaults (time 100, vrel 1e-6, gap, unproj (0,0,1), 1e10, 1e11 pivots) */
static void p3_ip(unsigned char *ip) {
  memset(ip, 0, 160);
  *(float *)(ip + 28) = 100.0f;
  SETI(ip, 32, 897988541);
  SETI(ip, 36, 1016003125);
  *(float *)(ip + 64) = 1.0f;
  SETI(ip, 68, 1343554297);
  for (int k = 0; k < 6; k++) SETI(ip, 72 + 4 * k, 1371161527);
}

static void p3_print_contact(unsigned char *c) {
  printf("c t %016llx p", *(unsigned long long *)c);
  out_f((float *)(c + 8), 3);
  printf(" n");
  out_f((float *)(c + 20), 3);
  printf(" id %d %d pr %d %d\n", *(int *)(c + 52), *(int *)(c + 56), *(int *)(c + 60), *(int *)(c + 64));
}


/* queued queries: event 9 / 10 callbacks (called by PumpLoggedEvents) */
static int p3_rwiq_cb(void *ev) {
  unsigned char *e = (unsigned char *)ev;
  unsigned char *hits = *(unsigned char **)(e + 48);
  int n = *(int *)(e + 56), nmax = *(int *)(e + 60);
  printf("rwiq %s n %d max %d pool %d\n", *(const char **)(e + 24), n, nmax, *(int *)(e + 64));
  for (int i = 0; i < nmax && hits; i++) {
    unsigned char *h = hits + 80 * i;
    if (*(float *)h < 0 && i > 0) continue;
    p3_print_hit(i, h);
  }
  return 1;
}
static int p3_pwiq_cb(void *ev) {
  unsigned char *e = (unsigned char *)ev;
  printf("pwiq %s d %08x e %s p", *(const char **)(e + 24), fbits(*(float *)(e + 48)), name_of(*(void **)(e + 16)));
  out_f((float *)(e + 52), 3);
  printf(" n");
  out_f((float *)(e + 64), 3);
  printf(" id %d pid %d\n", *(int *)(e + 76), *(int *)(e + 80));
  return 1;
}
static char *p3_strdup(const char *s) {
  char *d = (char *)malloc(strlen(s) + 1);
  strcpy(d, s);
  return d;
}

static int p3q_command(const char *c, const Toks *tp) {
  const Toks *t = tp;
  if (!strcmp(c, "rayx")) {
    unsigned char p[96];
    void *skip[16];
    memset(p, 0, sizeof p);
    int nmax = atoi(get(t, "max", "1"));
    if (nmax < 1) nmax = 1;
    if (nmax > 16) nmax = 16;
    unsigned char local[80 * 16];
    memset(local, 0, sizeof local);
    const char *keep = get(t, "keep", 0);
    unsigned char *hits = keep ? p3_buf(keep) : local;
    const char *hln = get(t, "hitlast", 0);
    unsigned char *hl = hln ? p3_hl(hln) : 0;
    int nskip = p3_skip(get(t, "skip", 0), skip, 16);
    parse_fv(need(t, "org"), (float *)(p + 24), 3);
    parse_fv(need(t, "dir"), (float *)(p + 36), 3);
    SETI(p, 48, parse_u(get(t, "types", "0x13F")));
    SETI(p, 52, parse_u(get(t, "flags", "0")));
    SETP(p, 56, hits);
    SETI(p, 64, nmax);
    SETP(p, 72, hl);
    SETI(p, 80, nskip);
    SETP(p, 88, nskip ? (void *)skip : 0);
    int n = ((fn_rwi)VT(g_world)[W_RayWorldIntersection])(g_world, p, "physoracle", atoi(get(t, "caller", "4")));
    printf("ray %s n %d\n", get(t, "name", "-"), n);
    for (int i = 0; i < nmax; i++) {
      unsigned char *h = hits + 80 * i;
      if (*(float *)h < 0 && i > 0) continue;
      p3_print_hit(i, h);
    }
    if (hl) printf("hl %s e %s ip %d nd %d\n", hln, name_of(*(void **)hl), *(int *)(hl + 8), *(int *)(hl + 12));
    return 1;
  }
  if (!strcmp(c, "pwi")) {
    unsigned char p[96], prim[256], ip[160];
    void *skip[64];
    void *pc = 0;
    memset(p, 0, sizeof p);
    int type = p3_prim(t, prim);
    const char *only = get(t, "only", 0);
    int nskip = p3_skip(only ? only : get(t, "skip", 0), skip, 64);
    SETI(p, 24, type);
    SETP(p, 32, prim);
    parse_fv(get(t, "sweep", "0,0,0"), (float *)(p + 40), 3);
    SETI(p, 52, parse_u(get(t, "types", "0x13F")));
    SETP(p, 56, &pc);
    SETI(p, 64, parse_u(get(t, "all", "0")));
    SETI(p, 68, parse_u(get(t, "any", "0xFFFFFFFF")));
    const char *stop = get(t, "stop", 0);
    if (stop) {
      p3_ip(ip);
      ip[98] = (unsigned char)atoi(stop);
      SETP(p, 72, ip);
    }
    SETI(p, 80, only ? -nskip : nskip);
    SETP(p, 88, nskip ? (void *)skip : 0);
    float d = ((fn_pwi)VT(g_world)[65])(g_world, p, "physoracle");
    printf("pwi %s d %08x\n", get(t, "name", "-"), fbits(d));
    if (d != 0 && pc) p3_print_contact((unsigned char *)pc);
    /* with caller params and 0 < t < 1e10 PWI returns with the caller lock still held and its
       address in ip+136 (the caller releases it after reading the contacts) */
    if (stop) {
      volatile long *lk = *(volatile long **)(ip + 136);
      if (lk && (*lk & 0xFFFF0000)) InterlockedExchangeAdd(lk, -0x10000);
    }
    return 1;
  }
  if (!strcmp(c, "rte")) {
    unsigned char h[80];
    float org[3], dir[3];
    memset(h, 0, sizeof h);
    void *e = find_target(need(t, "ent"));
    parse_fv(need(t, "org"), org, 3);
    parse_fv(need(t, "dir"), dir, 3);
    int r = ((fn_rte)VT(g_world)[61])(g_world, e, org, dir, h, 0, (int)parse_u(get(t, "any", "0xFFFFFFFF")),
                                     (char)atoi(get(t, "backfaces", "0")));
    printf("rte %s r %d\n", get(t, "name", "-"), r);
    if (r) p3_print_hit(0, h);
    return 1;
  }
  if (!strcmp(c, "cewp")) {
    unsigned char h[80], prim[256];
    float sw[3] = {0, 0, 0};
    memset(h, 0, sizeof h);
    void *e = find_target(need(t, "ent"));
    int type = p3_prim(t, prim);
    parse_fv(get(t, "sweep", "0,0,0"), sw, 3);
    int r = ((fn_cewp)VT(g_world)[60])(g_world, e, type, prim, sw, h, 0, (int)parse_u(get(t, "any", "0xFFFFFFFF")),
                                      (char)atoi(get(t, "inside", "0")));
    printf("cewp %s r %d\n", get(t, "name", "-"), r);
    if (r) p3_print_hit(0, h);
    return 1;
  }
  if (!strcmp(c, "beam")) {
    unsigned char h[80];
    float org[3], dir[3];
    memset(h, 0, sizeof h);
    void *e = find_target(need(t, "ent"));
    parse_fv(need(t, "org"), org, 3);
    parse_fv(need(t, "dir"), dir, 3);
    int r = ((fn_beam)VT(g_world)[59])(g_world, e, org, dir, parse_f(get(t, "r", "0.5")), h,
                                      (int)parse_u(get(t, "any", "0xFFFFFFFF")));
    printf("beam %s r %d\n", get(t, "name", "-"), r);
    if (r) p3_print_hit(0, h);
    return 1;
  }
  if (!strcmp(c, "rayq")) {
    unsigned char p[96];
    void *skip[16];
    memset(p, 0, sizeof p);
    int nmax = atoi(get(t, "max", "1"));
    if (nmax < 1) nmax = 1;
    if (nmax > 16) nmax = 16;
    int pool = atoi(get(t, "pool", "0"));
    int nskip = p3_skip(get(t, "skip", 0), skip, 16);
    SETP(p, 0, p3_strdup(get(t, "name", "-")));
    SETP(p, 16, (void *)p3_rwiq_cb);
    parse_fv(need(t, "org"), (float *)(p + 24), 3);
    parse_fv(need(t, "dir"), (float *)(p + 36), 3);
    SETI(p, 48, parse_u(get(t, "types", "0x13F")));
    SETI(p, 52, parse_u(get(t, "flags", "0")) | 0x800);
    SETP(p, 56, pool ? 0 : p3_buf(get(t, "name", "-")));
    SETI(p, 64, nmax);
    SETI(p, 80, nskip);
    SETP(p, 88, nskip ? (void *)skip : 0);
    int n = ((fn_rwi)VT(g_world)[W_RayWorldIntersection])(g_world, p, "physoracle", atoi(get(t, "caller", "4")));
    printf("rayq %s r %d\n", get(t, "name", "-"), n);
    return 1;
  }
  if (!strcmp(c, "pwiq")) {
    unsigned char p[96], prim[256];
    void *skip[16];
    memset(p, 0, sizeof p);
    int type = p3_prim(t, prim);
    int nskip = p3_skip(get(t, "skip", 0), skip, 16);
    SETP(p, 0, p3_strdup(get(t, "name", "-")));
    SETP(p, 16, (void *)p3_pwiq_cb);
    SETI(p, 24, type);
    SETP(p, 32, prim);
    parse_fv(get(t, "sweep", "0,0,0"), (float *)(p + 40), 3);
    SETI(p, 52, parse_u(get(t, "types", "0x13F")) | 0x800);
    SETI(p, 64, parse_u(get(t, "all", "0")));
    SETI(p, 68, parse_u(get(t, "any", "0xFFFFFFFF")));
    SETI(p, 80, nskip);
    SETP(p, 88, nskip ? (void *)skip : 0);
    float d = ((fn_pwi)VT(g_world)[65])(g_world, p, "physoracle");
    printf("pwiq %s queued %08x\n", get(t, "name", "-"), fbits(d));
    return 1;
  }
  if (!strcmp(c, "process")) {
    int n = ((int (*)(void *, int))VT(g_world)[43])(g_world, atoi(get(t, "mode", "1")));
    printf("process %d\n", n);
    return 1;
  }
  if (!strcmp(c, "pump")) {
    ((void (*)(void *))VT(g_world)[77])(g_world);
    printf("pump\n");
    return 1;
  }
  if (!strcmp(c, "watermat")) {
    ((void (*)(void *, int))VT(g_world)[88])(g_world, atoi(need(t, "v")));
    return 1;
  }
  if (!strcmp(c, "surface")) {
    int idx = atoi(need(t, "idx"));
    *(uint32_t *)((char *)g_world + 18792 + 4 * idx) = parse_u(need(t, "flags"));
    return 1;
  }
  if (!strcmp(c, "fdata")) {
    void *e = find_target(need(t, "target"));
    *(uint64_t *)((char *)e + 32) = (uint64_t)parse_u(need(t, "v"));
    *(int16_t *)((char *)e + 40) = (int16_t)atoi(get(t, "i", "0"));
    return 1;
  }
  return 0;
}
