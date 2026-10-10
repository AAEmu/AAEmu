/* P4a probe: the shared primitive Intersect (geometry slot 10, x64 0x35151F20) of the real
 * geometry objects, including its per-thread contact scratch. Hooked into the `--p2b` stdin
 * loop (one answer line per command). Included from physoracle.c after p2b_probe.c.
 *
 *   PI FILL <w> STALE <w> ST <nContacts> <nAreas> <nAreaPts> <nBorder> K <k>
 *      { C <tA> <primA words> <tB> <primB words> GA <22 gwd words> GB <22> IP <28 words> } x k
 *
 * Before the sequence the arena range [0xC7B8, 0x186D0) of the calling thread's scratch is
 * filled with `FILL ^ (i * 0x9E3779B9)` (i = word index) and the four counters are set. Each
 * call creates both geometries (CGeomManager::CreatePrimitive), fills the stack below the
 * caller with STALE (the Intersect frame's uninitialised locals then read STALE) and calls
 * vtable slot 10 of A with B. Answer:
 *
 *   (a call with bKeepPrevContacts and a negative contact or border count prints `| SKIP`)
 *   pi { | r <ret> P <slot|ffffffff> GA <offset xyz after> N <4 counters> [C <dump>] } x k
 *      H <hash [0xF2C4,0x10EC8)> <hash [0x16EC8,0x186D0)>
 *
 * dump = the 136-byte contact record (pointers +88/+96/+128 as arena offsets, 0 = null,
 * ffffffff = elsewhere), `B <border points via +88, at most 64>` or `B X`, then the area
 * (`A <record, pointers as offsets> <npt (<= 64) x pt xyz prim0 prim1 feat0 feat1>`) or NOAREA.
 * The Rust side is collision::prim_intersect::scratch::ThreadScratch::dump_contact.
 */

#define P4A_LO 0xC7B8u
#define P4A_HI 0x186D0u
#define P4A_STRIDE 237496ULL

static unsigned char *p4a_base;

static uint32_t p4a_ptr(uint64_t p) {
  uint64_t b = (uint64_t)p4a_base;
  if (!p) return 0;
  if (p >= b && p < b + P4A_STRIDE) return (uint32_t)(p - b);
  return 0xFFFFFFFFu;
}
static uint32_t p4a_rd(uint64_t addr) { return *(uint32_t *)(uintptr_t)addr; }
static int p4a_in(uint64_t p, uint64_t n) {
  uint64_t b = (uint64_t)p4a_base;
  return p >= b + P4A_LO && p + n <= b + P4A_HI;
}
static uint32_t p4a_hash(uint32_t from, uint32_t to) {
  uint32_t h = 0x811C9DC5u;
  for (uint32_t o = from; o < to; o++) h = (h ^ p4a_base[o]) * 0x01000193u;
  return h;
}

static void p4a_dump_contact(unsigned char *c) {
  printf(" C");
  for (int k = 0; k < 136;) {
    if (k == 88 || k == 96 || k == 128) {
      printf(" %08x", p4a_ptr(*(uint64_t *)(c + k)));
      k += 8;
    } else {
      printf(" %08x", *(uint32_t *)(c + k));
      k += 4;
    }
  }
  int nb = *(int *)(c + 104);
  uint64_t pb = *(uint64_t *)(c + 88);
  printf(" B");
  if (nb >= 0 && nb <= 64 && p4a_ptr(pb) != 0 && p4a_ptr(pb) != 0xFFFFFFFFu) {
    for (int q = 0; q < nb; q++)
      for (int j = 0; j < 3; j++) {
        uint64_t at = pb + 12ULL * q + 4ULL * j;
        printf(" %08x", p4a_in(at, 4) ? p4a_rd(at) : 0);
      }
  } else {
    printf(" X");
  }
  uint64_t pa = *(uint64_t *)(c + 128);
  uint32_t ao = p4a_ptr(pa);
  if (ao != 0 && ao != 0xFFFFFFFFu) {
    unsigned char *ar = p4a_base + ao;
    printf(" A");
    for (int k = 0; k < 72;) {
      if (k >= 16 && k <= 48) {
        printf(" %08x", p4a_ptr(*(uint64_t *)(ar + k)));
        k += 8;
      } else {
        printf(" %08x", *(uint32_t *)(ar + k));
        k += 4;
      }
    }
    int npt = *(int *)(ar + 4);
    if (npt < 0) npt = 0;
    if (npt > 64) npt = 64;
    for (int q = 0; q < npt; q++) {
      uint64_t pp = *(uint64_t *)(ar + 48) + 12ULL * q;
      for (int j = 0; j < 3; j++) printf(" %08x", p4a_in(pp + 4 * j, 4) ? p4a_rd(pp + 4 * j) : 0);
      static const int f[4] = {16, 24, 32, 40};
      for (int j = 0; j < 4; j++) {
        uint64_t x = *(uint64_t *)(ar + f[j]) + 4ULL * q;
        printf(" %08x", p4a_in(x, 4) ? p4a_rd(x) : 0);
      }
    }
  } else {
    printf(" NOAREA");
  }
}

typedef int (*p4a_isect_fn)(void *geom, void *other, float *gwd1, float *gwd2, void *ip, void **pcontacts);

static __declspec(noinline) int p4a_call(void *a, void *b, float *ga, float *gb, void *ip, void **pc, uint32_t stale) {
  p2b_fill_stack(stale);
  return ((p4a_isect_fn)VT(a)[10])(a, b, ga, gb, ip, pc);
}

static int p4a_ext_intersect(char *line) {
  char *cur = line;
  while (*cur == ' ' || *cur == '\t') cur++;
  /* earlier hooks may have tokenized the line in place ("PI" then a NUL) */
  if (cur[0] != 'P' || cur[1] != 'I' || (cur[2] != ' ' && cur[2] != '	' && cur[2] != 0)) return 0;
  cur += 3;
  p2b_world();
  DWORD tls = *(DWORD *)VA(g_phys, 0x3520F3A4ULL);
  void *tv = TlsGetValue(tls);
  int tid = tv ? *(int *)tv : 4;
  p4a_base = (unsigned char *)VA(g_phys, 0x35256F80ULL) + P4A_STRIDE * (uint64_t)tid;
  p2b_expect(&cur, "FILL");
  uint32_t fill = p2b_word(&cur);
  p2b_expect(&cur, "STALE");
  uint32_t stale = p2b_word(&cur);
  p2b_expect(&cur, "ST");
  uint32_t st[4];
  p2b_words(&cur, st, 4);
  p2b_expect(&cur, "K");
  int k = (int)p2b_word(&cur);
  for (uint32_t i = 0; i < (P4A_HI - P4A_LO) / 4; i++) ((uint32_t *)(p4a_base + P4A_LO))[i] = fill ^ (i * 0x9E3779B9u);
  *(uint32_t *)(p4a_base + 0xE9B8) = st[0];
  *(uint32_t *)(p4a_base + 0xF2C0) = st[1];
  *(uint32_t *)(p4a_base + 0x10EC4) = st[2];
  *(uint32_t *)(p4a_base + 0x16EC8) = st[3];
  printf("pi");
  for (int call = 0; call < k; call++) {
    p2b_expect(&cur, "C");
    uint32_t pa[16], pb[16];
    __declspec(align(16)) uint32_t ga[24], gb[24], ip[64];
    memset(ip, 0, sizeof ip);
    int ta = (int)p2b_word(&cur);
    int na = p2b_prim_words(ta);
    if (na < 0) die("p4a: bad type", "");
    p2b_words(&cur, pa, na);
    int tb = (int)p2b_word(&cur);
    int nb = p2b_prim_words(tb);
    if (nb < 0) die("p4a: bad type", "");
    p2b_words(&cur, pb, nb);
    p2b_expect(&cur, "GA");
    p2b_words(&cur, ga, 22);
    p2b_expect(&cur, "GB");
    p2b_words(&cur, gb, 22);
    p2b_expect(&cur, "IP");
    p2b_words(&cur, ip, 28);
    /* a negative contact / border count (only reachable after an overrun clobbered the
       counter) passes the gate and makes the original write outside the arena: skip */
    if ((ip[24] & 0xFF00) && ((*(int *)(p4a_base + 0xE9B8)) < 0 || (*(int *)(p4a_base + 0x16EC8)) < 0)) {
      printf(" | SKIP");
      continue;
    }
    void *a = p2b_geom(ta, pa), *b = p2b_geom(tb, pb);
    void *pc = 0;
    int ret = p4a_call(a, b, (float *)ga, (float *)gb, ip, &pc, stale);
    unsigned char *contacts = p4a_base + P4A_LO;
    uint32_t slot = 0xFFFFFFFFu;
    if (pc) slot = (uint32_t)(((unsigned char *)pc - contacts) / 136);
    printf(" | r %08x P %08x GA", (uint32_t)ret, slot);
    p2b_print_words(ga, 3);
    printf(" N %08x %08x %08x %08x", *(uint32_t *)(p4a_base + 0xE9B8), *(uint32_t *)(p4a_base + 0xF2C0),
           *(uint32_t *)(p4a_base + 0x10EC4), *(uint32_t *)(p4a_base + 0x16EC8));
    if (pc) p4a_dump_contact((unsigned char *)pc);
  }
  printf(" H %08x %08x\n", p4a_hash(0xF2C4, 0x10EC8), p4a_hash(0x16EC8, P4A_HI));
  return 1;
}
