/* P4b probe: drives the real contact solver (InvokeContactSolver 0x3509E850 with InitSolver
 * 0x3509A900, RegisterContact 0x350994B0, DisableCG 0x35099570) on caller-supplied islands.
 * Included by physoracle.c; entered with `physoracle.exe [--rt DIR] --p4b`, which reads one
 * command per stdin line and answers one line per command. All values are hex words.
 *
 * Setup: CreatePhysicalWorld(NULL), the fake gEnv, one TimeStep(0.01) on the empty world (sets
 * the physics-thread TLS slot, so the thread index is 0), then per command:
 *
 *   S <dt> <e0> <prev> <cg> <nvars> {<off> <word>}*nvars
 *     <nb> {53 words}*nb  D {53 words}  <nc> {44 words}*nc  <nmb> {14 words}*nmb
 *     [<nrep>]
 *
 *   dt, e0       floats (bits)
 *   prev         *a5 on entry
 *   cg           0 calls DisableCG after the contacts are registered
 *   vars         world offsets (104..631) and words written into the world's vars block for
 *                this command (restored afterwards)
 *   bodies       212-byte RigidBody images; the solver-index shorts (+200) are used as given
 *   D            the image of the thread-0 static dummy body (0x3520C390)
 *   contacts     176-byte images; pointer words are encoded: +0/+8/+16/+64/+72/+88 ignored (0),
 *                +96/+104 body index (-1 = the dummy), +168 = (kind, index): kind 0 null,
 *                1 = &contact[index]+164, 2 = &multibody[index]
 *   multibody    56-byte entries (rope constraints): +0 r, +12 n0, +24 n1, +36 unk, +40 body
 *                index (word 10; word 11 ignored), +48 idx, +52 pad
 *   nrep         optional: run the same island this many times in a row (solver block not
 *                cleared in between; the answer is for the last run)
 *
 * Bodies live in one array at a fixed high address (so the body address order equals the index
 * order and every body lies above the dummy), contacts in one array after it. The solver block
 * qword_352186E0[0] is zero-filled before each command (InitSolver allocates it uninitialised).
 *
 * Answer: `ret <eax> a5 <word> nbl <T+4> cl {contact index of T+8[i]}*nc bl {body index of
 * T+79880[j]}*T+4 B {53 words}*nb D {53 words} C {44 words}*nc M {14 words}*nmb` with the same
 * pointer encoding.
 */

#define P4B_DUMMY 0x3520C390ULL
#define P4B_TDATA 0x352186E0ULL
#define P4B_TSIZE 0x5FC030
#define P4B_MAXB 4096
#define P4B_MAXC 10240
#define P4B_MAXMB 1024

typedef int (*p4b_solve_fn)(float dt, void *vars, float e0, void **a4, int *a5, void **a6);
typedef void (*p4b_void_fn)(void);
typedef void (*p4b_reg_fn)(void *c);

static char *p4b_bodies, *p4b_contacts, *p4b_mb;

static char *p4b_tok(char **cur) {
  char *s = *cur;
  while (*s == ' ' || *s == '\t' || *s == '\r' || *s == '\n') s++;
  if (!*s) { *cur = s; return 0; }
  char *e = s;
  while (*e && *e != ' ' && *e != '\t' && *e != '\r' && *e != '\n') e++;
  if (*e) *e++ = 0;
  *cur = e;
  return s;
}
static uint32_t p4b_word(char **cur) {
  char *t = p4b_tok(cur);
  if (!t) die("p4b: missing word", "");
  return (uint32_t)strtoul(t, 0, 16);
}
static void p4b_expect(char **cur, const char *k) {
  char *t = p4b_tok(cur);
  if (!t || strcmp(t, k)) die("p4b: expected", k);
}

static void *p4b_body_ptr(int idx) {
  return idx < 0 ? VA(g_phys, P4B_DUMMY) : (void *)(p4b_bodies + 212 * (size_t)idx);
}
static int p4b_body_idx(void *p, int nb) {
  char *d = (char *)VA(g_phys, P4B_DUMMY);
  if ((char *)p >= d && (char *)p < d + 212 * 4) return -1 - (int)(((char *)p - d) / 212);
  ptrdiff_t o = (char *)p - p4b_bodies;
  if (o >= 0 && o < 212 * (ptrdiff_t)nb && o % 212 == 0) return (int)(o / 212);
  return -100;
}
static int p4b_contact_idx(void *p, int nc) {
  ptrdiff_t o = (char *)p - p4b_contacts;
  if (o >= 0 && o < 176 * (ptrdiff_t)nc && o % 176 == 0) return (int)(o / 176);
  return -100;
}

static void p4b_print_body(const uint32_t *w) {
  for (int k = 0; k < 53; k++) printf(" %08x", w[k]);
}
static void p4b_print_contact(char *c, int nb, int nc) {
  uint32_t *w = (uint32_t *)c;
  for (int k = 0; k < 44; k++) {
    uint32_t v = w[k];
    if (k <= 5 || (k >= 16 && k <= 19) || k == 22 || k == 23) v = 0;
    if (k == 24 || k == 26) v = (uint32_t)p4b_body_idx(*(void **)(c + 4 * k), nb);
    if (k == 25 || k == 27) v = 0;
    if (k == 42 || k == 43) {
      char *o = *(char **)(c + 168);
      uint32_t kind = 0, idx = 0;
      if (o) {
        int ci = p4b_contact_idx(o - 164, nc);
        if (ci >= 0) { kind = 1; idx = (uint32_t)ci; }
        else if (o >= p4b_mb && o < p4b_mb + 56 * P4B_MAXMB) { kind = 2; idx = (uint32_t)((o - p4b_mb) / 56); }
        else { kind = 3; }
      }
      v = k == 42 ? kind : idx;
    }
    printf(" %08x", v);
  }
}

static void p4b_command(char *line) {
  char *cur = line;
  char *op = p4b_tok(&cur);
  if (!op) { printf("\n"); return; }
  if (strcmp(op, "S")) die("p4b: unknown command", op);
  uint32_t dtw = p4b_word(&cur), e0w = p4b_word(&cur);
  int prev = (int)p4b_word(&cur);
  int cg = (int)p4b_word(&cur);
  int nvars = (int)p4b_word(&cur);
  int voff[64];
  uint32_t vsave[64];
  char *w = (char *)g_world;
  for (int i = 0; i < nvars && i < 64; i++) {
    voff[i] = (int)p4b_word(&cur);
    uint32_t v = p4b_word(&cur);
    vsave[i] = *(uint32_t *)(w + voff[i]);
    *(uint32_t *)(w + voff[i]) = v;
  }
  int nb = (int)p4b_word(&cur);
  if (nb > P4B_MAXB) die("p4b: too many bodies", "");
  memset(p4b_bodies, 0, 212 * (size_t)P4B_MAXB);
  for (int i = 0; i < nb; i++)
    for (int k = 0; k < 53; k++) ((uint32_t *)(p4b_bodies + 212 * (size_t)i))[k] = p4b_word(&cur);
  p4b_expect(&cur, "D");
  uint32_t *dummy = (uint32_t *)VA(g_phys, P4B_DUMMY);
  for (int k = 0; k < 53; k++) dummy[k] = p4b_word(&cur);
  int nc = (int)p4b_word(&cur);
  if (nc > P4B_MAXC) die("p4b: too many contacts", "");
  memset(p4b_contacts, 0, 176 * (size_t)P4B_MAXC);
  int okind[P4B_MAXC], oidx[P4B_MAXC];
  for (int i = 0; i < nc; i++) {
    char *c = p4b_contacts + 176 * (size_t)i;
    uint32_t *cw = (uint32_t *)c;
    for (int k = 0; k < 44; k++) cw[k] = p4b_word(&cur);
    for (int k = 0; k <= 5; k++) cw[k] = 0;
    for (int k = 16; k <= 19; k++) cw[k] = 0;
    cw[22] = cw[23] = 0;
    int i0 = (int)cw[24], i1 = (int)cw[26];
    *(void **)(c + 96) = p4b_body_ptr(i0);
    *(void **)(c + 104) = p4b_body_ptr(i1);
    okind[i] = (int)cw[42];
    oidx[i] = (int)cw[43];
  }
  for (int i = 0; i < nc; i++) {
    char *c = p4b_contacts + 176 * (size_t)i;
    char *o = 0;
    if (okind[i] == 1) o = p4b_contacts + 176 * (size_t)oidx[i] + 164;
    else if (okind[i] == 2) o = p4b_mb + 56 * (size_t)oidx[i];
    *(char **)(c + 168) = o;
  }
  int nmb = (int)p4b_word(&cur);
  if (nmb > P4B_MAXMB) die("p4b: too many multibody entries", "");
  memset(p4b_mb, 0, 56 * (size_t)P4B_MAXMB);
  for (int i = 0; i < nmb; i++) {
    uint32_t *e = (uint32_t *)(p4b_mb + 56 * (size_t)i);
    for (int k = 0; k < 14; k++) e[k] = p4b_word(&cur);
    *(void **)(e + 10) = p4b_body_ptr((int)e[10]);
  }
  char *t = p4b_tok(&cur);
  int nrep = t ? (int)strtoul(t, 0, 16) : 1;

  p4b_solve_fn solve = (p4b_solve_fn)VA(g_phys, 0x3509E850ULL);
  p4b_void_fn init = (p4b_void_fn)VA(g_phys, 0x3509A900ULL);
  p4b_void_fn nocg = (p4b_void_fn)VA(g_phys, 0x35099570ULL);
  p4b_reg_fn reg = (p4b_reg_fn)VA(g_phys, 0x350994B0ULL);
  void **tdata = (void **)VA(g_phys, P4B_TDATA);
  if (!tdata[0]) init();
  memset(tdata[0], 0, P4B_TSIZE);
  int ret = 0, a5 = prev;
  void *a4 = 0, *a6 = 0;
  for (int r = 0; r < nrep; r++) {
    init();
    for (int i = 0; i < nc; i++) reg(p4b_contacts + 176 * (size_t)i);
    if (!cg) nocg();
    a5 = prev;
    ret = solve(bitsf(dtw), w + 104, bitsf(e0w), &a4, &a5, &a6);
  }
  for (int i = 0; i < nvars && i < 64; i++) *(uint32_t *)(w + voff[i]) = vsave[i];

  char *T = (char *)tdata[0];
  int tn = *(int *)T, tb = *(int *)(T + 4);
  printf("ret %08x a5 %08x nbl %08x cl", (uint32_t)ret, (uint32_t)a5, (uint32_t)tb);
  for (int i = 0; i < tn && i < P4B_MAXC; i++) printf(" %08x", (uint32_t)p4b_contact_idx(*(void **)(T + 8 + 8 * (size_t)i), nc));
  printf(" bl");
  for (int j = 0; j < tb && j < P4B_MAXB; j++) printf(" %08x", (uint32_t)p4b_body_idx(*(void **)(T + 79880 + 8 * (size_t)j), nb));
  printf(" B");
  for (int i = 0; i < nb; i++) p4b_print_body((uint32_t *)(p4b_bodies + 212 * (size_t)i));
  printf(" D");
  p4b_print_body(dummy);
  printf(" C");
  for (int i = 0; i < nc; i++) p4b_print_contact(p4b_contacts + 176 * (size_t)i, nb, nc);
  printf(" M");
  for (int i = 0; i < nmb; i++) {
    uint32_t *e = (uint32_t *)(p4b_mb + 56 * (size_t)i);
    for (int k = 0; k < 14; k++) {
      uint32_t v = e[k];
      if (k == 10) v = (uint32_t)p4b_body_idx(*(void **)(e + 10), nb);
      if (k == 11) v = 0;
      printf(" %08x", v);
    }
  }
  printf("\n");
  fflush(stdout);
}

static void p4b_run(void) {
  g_world = ((void *(*)(void *))GetProcAddress(g_phys, "CreatePhysicalWorld"))(NULL);
  if (!g_world) { printf("ERROR CreatePhysicalWorld failed\n"); exit(1); }
  install_fake_env(g_phys);
  ((fn_timestep)VT(g_world)[W_TimeStep])(g_world, 0.01f, 0x9F);
  size_t total = 212 * (size_t)P4B_MAXB + 176 * (size_t)P4B_MAXC + 56 * (size_t)P4B_MAXMB + 3 * 4096;
  char *base = (char *)VirtualAlloc((void *)0x400000000ULL, total, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
  if (!base) base = (char *)VirtualAlloc(0, total, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
  if (!base || base < (char *)VA(g_phys, P4B_DUMMY)) die("p4b: cannot place the body array above the dummy", "");
  p4b_bodies = base;
  p4b_contacts = base + ((212 * (size_t)P4B_MAXB + 4095) & ~(size_t)4095);
  p4b_mb = p4b_contacts + ((176 * (size_t)P4B_MAXC + 4095) & ~(size_t)4095);
  printf("physoracle-p4b 1\n");
  static char line[1 << 24];
  while (fgets(line, sizeof line, stdin)) p4b_command(line);
  fflush(stdout);
}
