/* p1c_ray_probe: the CTriMesh ray path of the real x64 cryphysics_dedicate.dll — slot 22
 * PrepareForRayTest (0x351D7C40, grid builder 0x351C8B10), slot 10 Intersect (0x351C9910) with
 * a ray, slot 48 PreparePrimitive (0x351C2A60) and slot 49 UnprojectSphere (0x351D8EA0).
 * Oracle for tests/p1c_ray.rs. Build with tests/p1c_build_probe.bat.
 *
 *   p1c_ray_probe [--rt DIR] CASEFILE
 *
 * Case file (one command per line; hex words are raw IEEE bits / ints):
 *   mesh FILE FLAGS MINTRIS MAXTRIS MATSEED   build a DLL mesh (int indices, flag 2 | 0x20000
 *                                             added; MATSEED >= 0 adds per-triangle materials
 *                                             (signed char)((i*37 + MATSEED) % 61 - 20)) and
 *                                             print its dump
 *   prim 3 OX OY OZ DX DY DZ                  create a ray geometry
 *   prep M RAYLEN RESET                       slot 22 on mesh M (RESET 1: +476 = 0 first);
 *                                             prints "prep RET ready N" and every built layer:
 *                                             "grid K <20 record words, +36 skipped>",
 *                                             "gc K N <FNV-1a of the N cell starts> gt T
 *                                             <FNV-1a of the T cell triangles>"
 *   isect M P PRIO GWDA(23) GWDB(23) IP(36)   mesh M's slot 10 against ray P whose priority
 *                                             (+20) is set to PRIO for the call; GWD words:
 *                                             offset, R(9), scale, v, w, com, iStartNode ("-" =
 *                                             null; iStartNode fffffffe = the previous isect's
 *                                             first iPrim[0] + 1, or 0); IP: 36 dwords ("-" = null). Prints
 *                                             "res N arena K" and one "c ..." line per contact
 *   pprim M GWD(23)                           slot 48: "pp RET <12 words>"
 *   usph M CX CY CZ R RSEP TRI                slot 49 with the result's +40 preset to TRI ("L": the
 *                                             previous usph's result +40; the
 *                                             rest zero): "us RET <12 words> ready N center
 *                                             <3 words> arena K"
 * The per-caller arena (iCaller 4 without TLS) contact pool is zeroed before every isect. */
#include "p1c_common.h"

#define MAXOBJ (1 << 17)
static void *g_mesh[MAXOBJ], *g_prim[MAXOBJ];
static int g_nmesh, g_nprim;

static unsigned char *arena4(void) { return (unsigned char *)VA(0x35256F80ULL) + 4 * 0x39FB8; }

static void load_mesh(const char *path, unsigned flags, int minT, int maxT, int seed) {
  long len;
  unsigned char *b = p1c_read_file(path, &len);
  int nv = *(int *)b, nt = *(int *)(b + 4);
  float *v = (float *)malloc(12 * (size_t)nv + 12);
  int *idx = (int *)malloc(12 * (size_t)nt + 12);
  memcpy(v, b + 8, 12 * (size_t)nv);
  memcpy(idx, b + 8 + 12 * (size_t)nv, 12 * (size_t)nt);
  signed char *mats = NULL;
  if (seed >= 0) {
    mats = (signed char *)malloc((size_t)nt + 1);
    for (int i = 0; i < nt; i++) mats[i] = (signed char)((i * 37 + seed) % 61 - 20);
  }
  unsigned char *tm = p1c_mesh_new(v, idx, 4, mats, NULL, nt, (int)(flags | 0x20000 | 2), minT, maxT, 1.0f);
  g_mesh[g_nmesh++] = tm;
  p1c_mesh_dump(tm);
}

typedef void *(*fn_set)(void *, void *);
static void make_ray(uint32_t *w) {
  void *g = calloc(1, 0x70);
  ((fn_set)VA(0x3514D950ULL))(g, w);
  g_prim[g_nprim++] = g;
}

static void print_contact(unsigned char *c) {
  printf("c %016llx", (unsigned long long)*(uint64_t *)c);
  for (int i = 8; i < 44; i += 4) printf(" %08x", *(uint32_t *)(c + i));
  printf(" %d %08x %d %d %d %d %d %d %d %d", *(int *)(c + 44), *(uint32_t *)(c + 48), *(int *)(c + 52),
         *(int *)(c + 56), *(int *)(c + 60), *(int *)(c + 64), *(int *)(c + 68), *(int *)(c + 72),
         *(int *)(c + 76), *(int *)(c + 80));
  int nb = *(int *)(c + 104);
  float *bp = *(float **)(c + 88);
  const char *kind = (unsigned char *)bp == c + 8 ? "pt" : (unsigned char *)bp == c + 112 ? "center" : "pool";
  printf(" center %08x %08x %08x cons %d nb %d %s area %d\n", *(uint32_t *)(c + 112), *(uint32_t *)(c + 116),
         *(uint32_t *)(c + 120), c[124], nb, kind, *(void **)(c + 128) != NULL);
  if (nb > 1 || bp != (float *)(c + 8)) {
    printf("b");
    for (int i = 0; i < 3 * nb; i++) printf(" %08x", fb(bp[i]));
    printf("\n");
  }
}

/* FNV-1a 64 over the little-endian bytes of n ints. */
static uint64_t fnv(const int *p, int n) {
  uint64_t h = 0xcbf29ce484222325ULL;
  const unsigned char *b = (const unsigned char *)p;
  for (long i = 0; i < 4L * n; i++) { h ^= b[i]; h *= 0x100000001b3ULL; }
  return h;
}

static void print_grid(unsigned char *tm) {
  int n = *(int *)(tm + 476);
  for (int k = 0; k < n && k < 2; k++) {
    uint32_t *r = (uint32_t *)(tm + 224 + 84 * k);
    printf("grid %d", k);
    for (int i = 0; i < 21; i++)
      if (i != 9) printf(" %08x", r[i]);
    printf("\n");
    int nc = (int)r[17] * (int)r[18];
    int *cs = *(int **)(tm + 176 + 8 * k), *ct = *(int **)(tm + 200 + 8 * k);
    printf("gc %d %d %016llx gt %d %016llx\n", k, nc + 1, (unsigned long long)fnv(cs, nc + 1), cs[nc],
           (unsigned long long)fnv(ct, cs[nc]));
  }
}

typedef int (*fn_isect)(void *a, void *b, void *gwda, void *gwdb, void *ip, unsigned char **contacts);
typedef char (*fn_prep)(void *self, float raylen);
typedef int (*fn_pprim)(void *self, void *gwd, float **out, int icaller);
typedef int (*fn_usph)(void *self, float *center, float r, float rsep, void *res);

static int parse_words(char **tok, uint32_t *out, int n) {
  if (!strcmp(tok[0], "-")) return 0;
  for (int i = 0; i < n; i++) out[i] = (uint32_t)strtoul(tok[i], 0, 16);
  return n;
}

int main(int argc, char **argv) {
  p1c_init(&argc, &argv);
  if (argc < 2) { fprintf(stderr, "usage: p1c_ray_probe [--rt DIR] CASEFILE\n"); return 1; }
  FILE *f = fopen(argv[1], "r");
  if (!f) { printf("ERROR open %s\n", argv[1]); return 1; }
  static char line[1 << 16];
  char *tok[512];
  while (fgets(line, sizeof line, f)) {
    int nt = 0;
    for (char *p = strtok(line, " \t\r\n"); p && nt < 512; p = strtok(NULL, " \t\r\n")) tok[nt++] = p;
    if (!nt || tok[0][0] == '#') continue;
    if (!strcmp(tok[0], "mesh")) {
      load_mesh(tok[1], (unsigned)strtoul(tok[2], 0, 0), atoi(tok[3]), atoi(tok[4]), atoi(tok[5]));
    } else if (!strcmp(tok[0], "prim")) {
      uint32_t w[8] = {0};
      for (int i = 2; i < nt && i < 10; i++) w[i - 2] = (uint32_t)strtoul(tok[i], 0, 16);
      make_ray(w);
    } else if (!strcmp(tok[0], "prep")) {
      unsigned char *m = (unsigned char *)g_mesh[atoi(tok[1])];
      if (atoi(tok[3])) *(int *)(m + 476) = 0;
      char r = ((fn_prep)VSLOT(m, 22))(m, bf((uint32_t)strtoul(tok[2], 0, 16)));
      printf("prep %d ready %d\n", (int)r, *(int *)(m + 476));
      print_grid(m);
    } else if (!strcmp(tok[0], "isect")) {
      unsigned char *m = (unsigned char *)g_mesh[atoi(tok[1])];
      unsigned char *ray = (unsigned char *)g_prim[atoi(tok[2])];
      int prio = atoi(tok[3]);
      int k = 4;
      uint32_t gwda[24] = {0}, gwdb[24] = {0}, ip[64] = {0};
      int ha = parse_words(tok + k, gwda, 23); k += ha ? 23 : 1;
      int hb = parse_words(tok + k, gwdb, 23); k += hb ? 23 : 1;
      int hi = parse_words(tok + k, ip, 36); k += hi ? 36 : 1;
      unsigned char *ar = arena4();
      memset(ar + 0xC7B8, 0, 64 * 136);
      memset(ar + 0xE9C0, 0, 32 * 72);
      unsigned char *contacts = NULL;
      static int last_hit = 0;
      if (ha && gwda[22] == 0xFFFFFFFEu) gwda[22] = (uint32_t)last_hit;
      int old = *(int *)(ray + 20);
      *(int *)(ray + 20) = prio;
      int n = ((fn_isect)VSLOT(m, 10))(m, ray, ha ? gwda : NULL, hb ? gwdb : NULL, hi ? ip : NULL, &contacts);
      *(int *)(ray + 20) = old;
      last_hit = n > 0 ? *(int *)(contacts + 60) + 1 : 0;
      printf("res %d arena %d\n", n, *(int *)(ar + 0xE9B8));
      for (int i = 0; i < n; i++) print_contact(contacts + 136 * i);
    } else if (!strcmp(tok[0], "pprim")) {
      unsigned char *m = (unsigned char *)g_mesh[atoi(tok[1])];
      uint32_t gwd[24] = {0};
      parse_words(tok + 2, gwd, 23);
      float *out = NULL;
      int r = ((fn_pprim)VSLOT(m, 48))(m, gwd, &out, 4);
      printf("pp %d", r);
      for (int i = 0; i < 12; i++) printf(" %08x", fb(out[i]));
      printf("\n");
    } else if (!strcmp(tok[0], "usph")) {
      unsigned char *m = (unsigned char *)g_mesh[atoi(tok[1])];
      float c[3];
      for (int i = 0; i < 3; i++) c[i] = bf((uint32_t)strtoul(tok[2 + i], 0, 16));
      float r = bf((uint32_t)strtoul(tok[5], 0, 16)), rsep = bf((uint32_t)strtoul(tok[6], 0, 16));
      uint32_t res[12] = {0};
      static uint32_t last = 0;
      res[10] = tok[7][0] == 'L' ? last : (uint32_t)atoi(tok[7]);
      int ret = ((fn_usph)VSLOT(m, 49))(m, c, r, rsep, res);
      last = res[10];
      printf("us %d", ret);
      for (int i = 0; i < 12; i++) printf(" %08x", res[i]);
      printf(" ready %d center %08x %08x %08x arena %d\n", *(int *)(m + 476), *(uint32_t *)(m + 528),
             *(uint32_t *)(m + 532), *(uint32_t *)(m + 536), *(int *)(arena4() + 0xE9B8));
    } else {
      printf("ERROR unknown command %s\n", tok[0]);
      return 1;
    }
    fflush(stdout);
  }
  return 0;
}
