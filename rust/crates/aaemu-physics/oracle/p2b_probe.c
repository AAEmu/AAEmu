/* P2b probe: calls the UNPROJECT table cells (and, through the p2b_ext_*.c hooks, internal helper
 * routines) of the real cryphysics_dedicate.dll on caller-supplied inputs and prints every output
 * word as raw bits. Included by physoracle.c; entered with `physoracle.exe [--rt DIR] --p2b`, which
 * reads one command per stdin line and answers one line per command (same order).
 *
 * Unprojection command (all values are hex words, floats as raw IEEE bits):
 *
 *   U <mode> <tA> <tB> <featA> <featB> P <13 words> A <n words> B <n words> R <12 words> [AREA <7 words>]
 *
 *   P    unproj_params: mode, dir xyz, center xyz, vel, tmax, tmin, minPtDist, maxcos, flag
 *   A/B  primitives in their binary layout (box 16, tri 12, ray 6, sph 4, cyl/cap 8 words)
 *   R    initial unproj_result words: t (double, lo hi), cos (double, lo hi), pt xyz, n xyz, feat A B
 *   AREA geom_contact_area header: +0, npt, nmaxpt, minedge, n xyz; the arrays (pt, piPrim[2],
 *        piFeature[2]) get nmaxpt (<= 1024) entries pre-filled with 0xDEAD0000+i / 0xBEEF0000+i.
 *        Without AREA the area pointer is NULL.
 *
 * Answer:
 *
 *   ret <word> P <13 words> R <12 words> [AREA <7 words> <min(npt,nmaxpt) x (pt xyz, prim0, prim1, feat0, feat1)>]
 *
 * The function pointer is read from the engine's own table at 0x35209FA0 ([2][8][8]), so the probe
 * runs exactly what the engine dispatches. Extension hooks: each P2b work group owns one
 * p2b_ext_<group>.c that defines `static int p2b_ext_<group>(char *line)` (return 1 when the line
 * was handled); they are tried in order for lines that are not `U` commands.
 */

#define P2B_UNPROJ_TABLE 0x35209FA0ULL
#define P2B_MAXAREA 1024

typedef int (*p2b_unproj_fn)(void *params, void *primA, int featA, void *primB, int featB, void *out, void *area);

static int p2b_prim_words(int type) {
  switch (type) {
  case 0: return 16; /* box */
  case 1: return 12; /* triangle */
  case 3: return 6;  /* ray */
  case 4: return 4;  /* sphere */
  case 5: case 6: return 8; /* cylinder, capsule */
  }
  return -1;
}

/* whitespace tokenizer that keeps a cursor (strtok is used by the hooks' callers) */
static char *p2b_tok(char **cur) {
  char *s = *cur;
  while (*s == ' ' || *s == '\t' || *s == '\r' || *s == '\n') s++;
  if (!*s) { *cur = s; return 0; }
  char *e = s;
  while (*e && *e != ' ' && *e != '\t' && *e != '\r' && *e != '\n') e++;
  if (*e) *e++ = 0;
  *cur = e;
  return s;
}
static uint32_t p2b_word(char **cur) {
  char *t = p2b_tok(cur);
  if (!t) die("p2b: missing word", "");
  return (uint32_t)strtoul(t, 0, 16);
}
static void p2b_expect(char **cur, const char *key) {
  char *t = p2b_tok(cur);
  if (!t || strcmp(t, key)) die("p2b: expected", key);
}
static void p2b_words(char **cur, uint32_t *w, int n) {
  for (int i = 0; i < n; i++) w[i] = p2b_word(cur);
}
static void p2b_print_words(const uint32_t *w, int n) {
  for (int i = 0; i < n; i++) printf(" %08x", w[i]);
}

/* helpers for the extension hooks */
static void *p2b_fn(uint64_t va) { return VA(g_phys, va); }

#include "p2b_ext_poly.c"
#include "p2b_ext_box_box.c"
#include "p2b_ext_box_cyl.c"
#include "p2b_ext_cyl_cyl.c"
#include "p2b_ext_box_round.c"
#include "p2b_ext_tri.c"
#include "p2b_ext_tri_cyl.c"
#include "p2b_ext_ray_cyl.c"
#include "p2b_ext_rot_tri.c"
#include "p2b_ext_rot_box.c"
#include "p2b_ext_rot_cyl.c"
#include "p2b_ext_rot_cap.c"
#include "p2b_ext_sweep.c"

static void p2b_unproj(char *cur) {
  __declspec(align(16)) uint32_t params[16], primA[16], primB[16], res[16];
  __declspec(align(16)) uint32_t hdr[18];
  static __declspec(align(16)) float pts[P2B_MAXAREA * 3];
  static int prim0[P2B_MAXAREA], prim1[P2B_MAXAREA], feat0[P2B_MAXAREA], feat1[P2B_MAXAREA];
  memset(params, 0, sizeof params);
  memset(primA, 0, sizeof primA);
  memset(primB, 0, sizeof primB);
  memset(res, 0, sizeof res);
  memset(hdr, 0, sizeof hdr);
  int mode = (int)p2b_word(&cur), ta = (int)p2b_word(&cur), tb = (int)p2b_word(&cur);
  int fa = (int)p2b_word(&cur), fb = (int)p2b_word(&cur);
  if (mode < 0 || mode > 1 || ta < 0 || ta > 7 || tb < 0 || tb > 7) die("p2b: bad cell", "");
  int na = p2b_prim_words(ta), nb = p2b_prim_words(tb);
  if (na < 0 || nb < 0) die("p2b: bad primitive type", "");
  p2b_expect(&cur, "P");
  p2b_words(&cur, params, 13);
  p2b_expect(&cur, "A");
  p2b_words(&cur, primA, na);
  p2b_expect(&cur, "B");
  p2b_words(&cur, primB, nb);
  p2b_expect(&cur, "R");
  p2b_words(&cur, res, 12);
  int has_area = 0;
  char *t = p2b_tok(&cur);
  if (t && !strcmp(t, "AREA")) {
    has_area = 1;
    uint32_t h[7];
    p2b_words(&cur, h, 7);
    /* geom_contact_area: +0 int, +4 npt, +8 nmaxpt, +12 minedge, +16 piPrim[0], +24 piPrim[1],
       +32 piFeature[0], +40 piFeature[1], +48 pt, +56 n */
    hdr[0] = h[0]; hdr[1] = h[1]; hdr[2] = h[2]; hdr[3] = h[3];
    int nmax = (int)h[2];
    if (nmax < 0 || nmax > P2B_MAXAREA) die("p2b: nmaxpt out of range", "");
    for (int i = 0; i < P2B_MAXAREA; i++) {
      pts[i * 3] = bitsf(0xDEAD0000u + i); pts[i * 3 + 1] = bitsf(0xDEAD4000u + i); pts[i * 3 + 2] = bitsf(0xDEAD8000u + i);
      prim0[i] = (int)(0xBEEF0000u + i); prim1[i] = (int)(0xBEEF4000u + i);
      feat0[i] = (int)(0xBEEF8000u + i); feat1[i] = (int)(0xBEEFC000u + i);
    }
    unsigned char *a = (unsigned char *)hdr;
    *(int **)(a + 16) = prim0; *(int **)(a + 24) = prim1;
    *(int **)(a + 32) = feat0; *(int **)(a + 40) = feat1;
    *(float **)(a + 48) = pts;
    memcpy(a + 56, &h[4], 12);
  }
  p2b_unproj_fn f = *(p2b_unproj_fn *)VA(g_phys, P2B_UNPROJ_TABLE + 8ULL * (mode * 64 + ta * 8 + tb));
  int ret = f(params, primA, fa, primB, fb, res, has_area ? (void *)hdr : 0);
  printf("ret %08x P", (uint32_t)ret);
  p2b_print_words(params, 13);
  printf(" R");
  p2b_print_words(res, 12);
  if (has_area) {
    unsigned char *a = (unsigned char *)hdr;
    uint32_t h[7];
    memcpy(h, a, 16);
    memcpy(&h[4], a + 56, 12);
    printf(" AREA");
    p2b_print_words(h, 7);
    int n = (int)h[1];
    if (n > (int)h[2]) n = (int)h[2];
    for (int i = 0; i < n; i++) {
      printf(" %08x %08x %08x %08x %08x %08x %08x", fbits(pts[i * 3]), fbits(pts[i * 3 + 1]), fbits(pts[i * 3 + 2]),
             (uint32_t)prim0[i], (uint32_t)prim1[i], (uint32_t)feat0[i], (uint32_t)feat1[i]);
    }
  }
  printf("\n");
}

static void p2b_run(void) {
  static char line[1 << 16];
  printf("physoracle-p2b 1\n");
  while (fgets(line, sizeof line, stdin)) {
    char *cur = line;
    char *probe = line;
    while (*probe == ' ' || *probe == '\t') probe++;
    if (!*probe || *probe == '\n' || *probe == '\r' || *probe == '#') { printf("\n"); continue; }
    if (probe[0] == 'U' && (probe[1] == ' ' || probe[1] == '\t')) {
      cur = probe + 2;
      p2b_unproj(cur);
    } else if (p2b_ext_poly(line) || p2b_ext_box_box(line) || p2b_ext_box_cyl(line) || p2b_ext_cyl_cyl(line) ||
               p2b_ext_box_round(line) || p2b_ext_tri(line) || p2b_ext_tri_cyl(line) || p2b_ext_ray_cyl(line) ||
               p2b_ext_rot_tri(line) || p2b_ext_rot_box(line) || p2b_ext_rot_cyl(line) || p2b_ext_rot_cap(line) ||
               p2b_ext_sweep(line)) {
    } else {
      die("p2b: unknown command", line);
    }
  }
  fflush(stdout);
}
