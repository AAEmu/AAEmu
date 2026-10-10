/* p1c_isect_probe: the generic CGeometry::Intersect (0x3514FD70) and CTriMesh::Intersect
 * (slot 10, 0x351C9910) of the real x64 cryphysics_dedicate.dll on cases read from a file.
 * Oracle for tests/p1c_intersect.rs. Build with tests/p1c_build_probe.bat.
 *
 *   p1c_isect_probe [--rt DIR] CASEFILE
 *
 * Case file (one command per line, hex words are raw IEEE bits / ints):
 *   mesh FILE FLAGS MINTRIS MAXTRIS         build a DLL mesh (ctor + 0x351CF1B0, int indices
 *                                           with flag 2 | 0x20000 added) and print its dump
 *   prim TYPE W0 W1 ...                     create a primitive geometry (ctor + SetData)
 *   isect A B FN GWDA(23) GWDB(23) IP(36)   run an intersection; A/B are "m<k>" (k-th mesh)
 *                                           or "p<k>" (k-th primitive); FN 0 = CGeometry slot
 *                                           10 directly, 1 = A's vtable slot 10. GWD words:
 *                                           offset, R(9), scale, v, w, com, iStartNode; a GWD
 *                                           of a single "-" is a null pointer. IP words are the
 *                                           first 36 dwords of intersection_params ("-" = null).
 *   meshmat FILE FLAGS MINTRIS MAXTRIS M0..  as mesh, with per-triangle material ids (P1c-register)
 *   verbose N                               N=1: after each contact an "x <+108>" line, after
 *                                           each res an "ipx" line (ip axis +16, ip+112) (P1c-register)
 *   poly CLOSED N0 X Y.. N1 X Y..           sub_35110F50 on two 2D polygons (N points + the
 *                                           closing copy each, hex words); prints
 *                                           "poly R SRC" (SRC 0/1 = an input, 2 = own buffer)
 *                                           then R "pp X Y ID" lines (P1c-register)
 * Output per isect: "res N convex C" then one "c ..." line per contact (see print_contact) and
 * its border / area lines. The per-caller arena (iCaller 4 without TLS) contact and area pools
 * are zeroed before every call so that fields the DLL does not write read as 0. */
#include "p1c_common.h"

#define MAXOBJ 256
static void *g_mesh[MAXOBJ], *g_prim[MAXOBJ];
static int g_nmesh, g_nprim;

static unsigned char *arena4(void) { return (unsigned char *)VA(0x35256F80ULL) + 4 * 0x39FB8; }

static int g_verbose;
static void load_mesh_m(const char *path, unsigned flags, int minT, int maxT, signed char *mats);
static void load_mesh(const char *path, unsigned flags, int minT, int maxT) { load_mesh_m(path, flags, minT, maxT, NULL); }
static void load_mesh_m(const char *path, unsigned flags, int minT, int maxT, signed char *mats) {
  long len;
  unsigned char *b = p1c_read_file(path, &len);
  int nv = *(int *)b, nt = *(int *)(b + 4);
  float *v = (float *)malloc(12 * (size_t)nv + 12);
  int *idx = (int *)malloc(12 * (size_t)nt + 12);
  memcpy(v, b + 8, 12 * (size_t)nv);
  memcpy(idx, b + 8 + 12 * (size_t)nv, 12 * (size_t)nt);
  unsigned char *tm = p1c_mesh_new(v, idx, 4, mats, NULL, nt, (int)(flags | 0x20000 | 2), minT, maxT, 1.0f);
  g_mesh[g_nmesh++] = tm;
  p1c_mesh_dump(tm);
}

typedef void *(*fn_ctor)(void *);
typedef void *(*fn_set)(void *, void *);
static void make_prim(int type, uint32_t *w) {
  void *g = NULL;
  switch (type) {
  case 0: g = calloc(1, 0xC8); ((fn_ctor)VA(0x35075190ULL))(g); ((fn_set)VA(0x3513A590ULL))(g, w); break;
  case 3: g = calloc(1, 0x70); ((fn_set)VA(0x3514D950ULL))(g, w); break;
  case 4: g = calloc(1, 0x98); ((fn_ctor)VA(0x35038C20ULL))(g); ((fn_set)VA(0x351AE9E0ULL))(g, w); break;
  case 5: g = calloc(1, 0xB0); ((fn_ctor)VA(0x35038960ULL))(g); ((fn_set)VA(0x35144DB0ULL))(g, w); break;
  case 6: g = calloc(1, 0xB0); ((fn_ctor)VA(0x35038AB0ULL))(g); ((fn_set)VA(0x35141920ULL))(g, w); break;
  default: printf("ERROR prim type %d\n", type); exit(1);
  }
  g_prim[g_nprim++] = g;
}

static void *obj(const char *s) {
  int k = atoi(s + 1);
  return s[0] == 'm' ? g_mesh[k] : g_prim[k];
}

static void print_contact(unsigned char *c) {
  printf("c %016llx", (unsigned long long)*(uint64_t *)c);
  for (int i = 8; i < 44; i += 4) printf(" %08x", *(uint32_t *)(c + i)); /* pt n dir */
  printf(" %d %08x %d %d %d %d %d %d %d %d", *(int *)(c + 44), *(uint32_t *)(c + 48), *(int *)(c + 52),
         *(int *)(c + 56), *(int *)(c + 60), *(int *)(c + 64), *(int *)(c + 68), *(int *)(c + 72),
         *(int *)(c + 76), *(int *)(c + 80));
  int nb = *(int *)(c + 104);
  float *bp = *(float **)(c + 88);
  const char *kind = (unsigned char *)bp == c + 8 ? "pt" : (unsigned char *)bp == c + 112 ? "center" : "pool";
  printf(" center %08x %08x %08x cons %d nb %d %s\n", *(uint32_t *)(c + 112), *(uint32_t *)(c + 116),
         *(uint32_t *)(c + 120), c[124], nb, kind);
  if (nb > 1 || bp != (float *)(c + 8)) {
    printf("b");
    for (int i = 0; i < 3 * nb; i++) printf(" %08x", fb(bp[i]));
    printf("\n");
    int *ib = *(int **)(c + 96);
    if (ib) {
      printf("bi");
      for (int i = 0; i < 2 * nb; i++) printf(" %d", ib[i]);
      printf("\n");
    }
  }
  unsigned char *a = *(unsigned char **)(c + 128);
  if (a) {
    int npt = *(int *)(a + 4);
    printf("a %d %d %d %08x n %08x %08x %08x\n", *(int *)a, npt, *(int *)(a + 8), *(uint32_t *)(a + 12),
           *(uint32_t *)(a + 56), *(uint32_t *)(a + 60), *(uint32_t *)(a + 64));
    float *pt = *(float **)(a + 48);
    int *p0 = *(int **)(a + 16), *p1 = *(int **)(a + 24), *f0 = *(int **)(a + 32), *f1 = *(int **)(a + 40);
    for (int i = 0; i < npt; i++)
      printf("ap %08x %08x %08x %d %d %d %d\n", fb(pt[3 * i]), fb(pt[3 * i + 1]), fb(pt[3 * i + 2]), p0[i], f0[i],
             p1[i], f1[i]);
  }
  if (g_verbose) printf("x %d\n", *(int *)(c + 108));
}

typedef int (*fn_poly)(long long, float *, int, float *, int, int, float **, int **);
static void run_poly(char **tok, int nt) {
  static float a[2][2 * 1100];
  int k = 1, n[2];
  int closed = atoi(tok[k++]);
  for (int p = 0; p < 2; p++) {
    n[p] = atoi(tok[k++]);
    for (int i = 0; i < 2 * (n[p] + 1) && k < nt; i++) ((uint32_t *)a[p])[i] = (uint32_t)strtoul(tok[k++], 0, 16);
  }
  float *outp = NULL;
  int *ids = NULL;
  int r = ((fn_poly)VA(0x35110F50ULL))(1, a[0], n[0], a[1], n[1], closed, &outp, &ids);
  int src = outp == a[0] ? 0 : outp == a[1] ? 1 : 2;
  printf("poly %d %d\n", r, src);
  for (int i = 0; i < r; i++) printf("pp %08x %08x %d\n", fb(outp[2 * i]), fb(outp[2 * i + 1]), ids[i]);
}

typedef int (*fn_isect)(void *a, void *b, void *gwda, void *gwdb, void *ip, unsigned char **contacts);

static int parse_words(char **tok, uint32_t *out, int n) {
  if (!strcmp(tok[0], "-")) return 0;
  for (int i = 0; i < n; i++) out[i] = (uint32_t)strtoul(tok[i], 0, 16);
  return n;
}

int main(int argc, char **argv) {
  p1c_init(&argc, &argv);
  if (argc < 2) { fprintf(stderr, "usage: p1c_isect_probe [--rt DIR] CASEFILE\n"); return 1; }
  FILE *f = fopen(argv[1], "r");
  if (!f) { printf("ERROR open %s\n", argv[1]); return 1; }
  static char line[1 << 22];
  static char *tok[1 << 17];
  while (fgets(line, sizeof line, f)) {
    int nt = 0;
    for (char *p = strtok(line, " \t\r\n"); p && nt < (1 << 17); p = strtok(NULL, " \t\r\n")) tok[nt++] = p;
    if (!nt || tok[0][0] == '#') continue;
    if (!strcmp(tok[0], "meshmat")) {
      static signed char mats[1 << 16];
      for (int i = 5; i < nt; i++) mats[i - 5] = (signed char)atoi(tok[i]);
      load_mesh_m(tok[1], (unsigned)strtoul(tok[2], 0, 0), atoi(tok[3]), atoi(tok[4]), mats);
    } else if (!strcmp(tok[0], "verbose")) {
      g_verbose = atoi(tok[1]);
    } else if (!strcmp(tok[0], "poly")) {
      run_poly(tok, nt);
    } else if (!strcmp(tok[0], "mesh")) {
      load_mesh(tok[1], (unsigned)strtoul(tok[2], 0, 0), atoi(tok[3]), atoi(tok[4]));
    } else if (!strcmp(tok[0], "prim")) {
      uint32_t w[32] = {0};
      for (int i = 2; i < nt; i++) w[i - 2] = (uint32_t)strtoul(tok[i], 0, 16);
      make_prim(atoi(tok[1]), w);
    } else if (!strcmp(tok[0], "isect")) {
      void *a = obj(tok[1]), *b = obj(tok[2]);
      int fnk = atoi(tok[3]);
      int k = 4;
      uint32_t gwda[24] = {0}, gwdb[24] = {0}, ip[64] = {0};
      int ha = parse_words(tok + k, gwda, 23); k += ha ? 23 : 1;
      int hb = parse_words(tok + k, gwdb, 23); k += hb ? 23 : 1;
      int hi = parse_words(tok + k, ip, 36); k += hi ? 36 : 1;
      unsigned char *ar = arena4();
      memset(ar + 0xC7B8, 0, 64 * 136);
      memset(ar + 0xE9C0, 0, 32 * 72);
      unsigned char *contacts = NULL;
      fn_isect fn = fnk ? (fn_isect)VSLOT(a, 10) : (fn_isect)VA(0x3514FD70ULL);
      int n = fn(a, b, ha ? gwda : NULL, hb ? gwdb : NULL, hi ? ip : NULL, &contacts);
      printf("res %d convex %d gwdav %08x %08x %08x ipv %08x sweep %d\n", n, hi ? (int)ip[28] : -1, gwda[13], gwda[14],
             gwda[15], ip[8], hi ? (int)(ip[24] & 0xFF) : -1);
      if (g_verbose) {
        if (hi) printf("ipx %08x %08x %08x %d\n", ip[4], ip[5], ip[6], (int)ip[28]);
        else printf("ipx -\n");
      }
      for (int i = 0; i < n; i++) print_contact(contacts + 136 * i);
    } else {
      printf("ERROR unknown command %s\n", tok[0]);
      return 1;
    }
    fflush(stdout);
  }
  return 0;
}
