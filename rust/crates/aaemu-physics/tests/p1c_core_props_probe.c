/* p1c_core_props_probe.c: CTriMesh property / accessor slots of the real x64
 * cryphysics_dedicate.dll (10.0.2.13), for tests/p1c_core_props.rs.
 *
 *   p1c_core_props_probe [--rt DIR] mesh <file.mesh> <flags> <minTris> <maxTris> <obbFactorHex> <aux> [cmd ...]
 *       builds a mesh (ctor + 0x351CF1B0; int indices used in place, so pass flag 2) from a
 *       p1c mesh file; aux bit 1 adds per-triangle materials, bit 2 foreign ids (generated
 *       below); prints the mesh dump, then runs the commands.
 *   p1c_core_props_probe [--rt DIR] blobs <file>
 *       file = records {u32 len, blob (CGF physics blob of a trimesh), u32 len, command text};
 *       each blob is loaded with slot 26 (stream after the geometry type id), dumped, and its
 *       commands run; every record starts with a "case <k>" line.
 *
 * Commands (each prints at most a few lines; floats as raw bits):
 *   dump | bbox | props | vol | center | ext <form> | cache | reset | setflags <hex>
 *   prim <i> | feat <i> <code> | acc <i> | misc | setfd <hexptr> <id> | destroy <mask>
 *   compact | remap <n> <cur..> <new..> | append <n> <withmap> <3n hex words> [<n ints>]
 *   seed <u32> | rand <form> <count> | clone | self
 */
#include "p1c_common.h"

typedef void (*fn_seed)(unsigned);
static fn_seed g_seed;

static void cache_line(unsigned char *tm) { prw("cache", tm + 516, 6); }

static int next_tok(char **p, char *out, int cap) {
  char *s = *p;
  while (*s == ' ' || *s == '\n' || *s == '\r' || *s == '\t') s++;
  if (!*s) return 0;
  int n = 0;
  while (*s && *s != ' ' && *s != '\n' && *s != '\r' && *s != '\t') {
    if (n < cap - 1) out[n++] = *s;
    s++;
  }
  out[n] = 0;
  *p = s;
  return 1;
}

typedef struct { char **v; int n, i; } Toks;
static const char *tk(Toks *t) {
  if (t->i >= t->n) { printf("ERROR missing token\n"); exit(2); }
  return t->v[t->i++];
}
static long long tki(Toks *t) { return strtoll(tk(t), NULL, 0); }
static unsigned tkx(Toks *t) { return (unsigned)strtoul(tk(t), NULL, 16); }

static void run(unsigned char *tm, Toks *t) {
  while (t->i < t->n) {
    const char *c = tk(t);
    if (!strcmp(c, "dump")) {
      p1c_mesh_dump(tm);
    } else if (!strcmp(c, "bbox")) {
      uint32_t b[16];
      memset(b, 0, sizeof b);
      ((void (*)(void *, void *))VSLOT(tm, 6))(tm, b);
      prw("bbox", b, 16);
    } else if (!strcmp(c, "props")) {
      unsigned char pg[96];
      memset(pg, 0xCD, sizeof pg);
      int r = ((int (*)(void *, void *))VSLOT(tm, 7))(tm, pg);
      printf("props %d", r);
      prw("", pg + 8, 11);
    } else if (!strcmp(c, "vol")) {
      float v = ((float (*)(void *))VSLOT(tm, 31))(tm);
      printf("vol %08x\n", fb(v));
    } else if (!strcmp(c, "center")) {
      float o[3];
      ((void *(*)(void *, float *))VSLOT(tm, 32))(tm, o);
      prw("center", o, 3);
    } else if (!strcmp(c, "ext")) {
      int f = (int)tki(t);
      float v = ((float (*)(void *, void *, int))VSLOT(tm, 42))(tm, NULL, f);
      printf("ext %d %08x\n", f, fb(v));
    } else if (!strcmp(c, "cache")) {
      cache_line(tm);
    } else if (!strcmp(c, "reset")) {
      for (int k = 0; k < 4; k++) *(uint32_t *)(tm + 516 + 4 * k) = 0xFFBFFFFFu;
    } else if (!strcmp(c, "setflags")) {
      *(uint32_t *)(tm + 152) = tkx(t);
    } else if (!strcmp(c, "prim")) {
      int i = (int)tki(t);
      uint32_t w[12];
      memset(w, 0, sizeof w);
      int r = ((int (*)(void *, int, void *))VSLOT(tm, 17))(tm, i, w);
      printf("prim %d %d", i, r);
      if (r) prw("", w, 12); else printf("\n");
    } else if (!strcmp(c, "feat")) {
      int i = (int)tki(t), code = (int)tki(t);
      uint32_t w[9];
      memset(w, 0, sizeof w);
      int r = ((int (*)(void *, int, int, void *))VSLOT(tm, 20))(tm, i, code, w);
      printf("feat %d %d %d", i, code, r);
      prw("", w, 9);
    } else if (!strcmp(c, "acc")) {
      int i = (int)tki(t);
      int m = ((int (*)(void *, int))VSLOT(tm, 16))(tm, i);
      int f = ((int (*)(void *, int))VSLOT(tm, 18))(tm, i);
      float n[3];
      ((void *(*)(void *, float *, int))VSLOT(tm, 19))(tm, n, i);
      printf("acc %d %d %d", i, m, f);
      prw("", n, 3);
    } else if (!strcmp(c, "misc")) {
      int nt = ((int (*)(void *))VSLOT(tm, 28))(tm);
      int e = ((int (*)(void *))VSLOT(tm, 34))(tm);
      int b = ((int (*)(void *))VSLOT(tm, 38))(tm);
      int ifd = *(int *)(tm + 40);
      void *(*gfd)(void *, int) = (void *(*)(void *, int))VSLOT(tm, 35);
      printf("misc %d %d %d %llx %llx %llx %llx\n", nt, e, b, (unsigned long long)gfd(tm, -1),
             (unsigned long long)gfd(tm, 0), (unsigned long long)gfd(tm, ifd), (unsigned long long)gfd(tm, ifd + 1));
    } else if (!strcmp(c, "setfd")) {
      unsigned long long p = strtoull(tk(t), NULL, 16);
      int id = (int)tki(t);
      ((void (*)(void *, void *, int))VSLOT(tm, 37))(tm, (void *)p, id);
    } else if (!strcmp(c, "destroy")) {
      int m = (int)tki(t);
      ((void (*)(void *, int))VSLOT(tm, 39))(tm, m);
    } else if (!strcmp(c, "compact")) {
      ((void (*)(void *))VSLOT(tm, 45))(tm);
    } else if (!strcmp(c, "remap")) {
      int n = (int)tki(t);
      int *cur = (int *)malloc(4 * (n + 1)), *nw = (int *)malloc(4 * (n + 1));
      for (int k = 0; k < n; k++) cur[k] = (int)tki(t);
      for (int k = 0; k < n; k++) nw[k] = (int)tki(t);
      ((void (*)(void *, int *, int *, int))VSLOT(tm, 40))(tm, cur, nw, n);
    } else if (!strcmp(c, "append")) {
      int n = (int)tki(t), wm = (int)tki(t);
      uint32_t *v = (uint32_t *)malloc(12 * (n + 1));
      int *map = (int *)malloc(4 * (n + 1));
      for (int k = 0; k < 3 * n; k++) v[k] = tkx(t);
      if (wm) for (int k = 0; k < n; k++) map[k] = (int)tki(t);
      ((void (*)(void *, void *, int *, int))VSLOT(tm, 41))(tm, v, wm ? map : NULL, n);
    } else if (!strcmp(c, "seed")) {
      g_seed((unsigned)strtoul(tk(t), NULL, 0));
    } else if (!strcmp(c, "rand")) {
      int f = (int)tki(t), cnt = (int)tki(t);
      for (int k = 0; k < cnt; k++) {
        uint32_t w[6];
        memset(w, 0, sizeof w);
        ((void (*)(void *, void *, void *, int))VSLOT(tm, 43))(tm, w, NULL, f);
        printf("rand %d", f);
        prw("", w, 6);
      }
    } else if (!strcmp(c, "clone")) {
      unsigned char *cl = ((unsigned char *(*)(void *, int))VSLOT(tm, 50))(tm, 1);
      printf("clone\n");
      p1c_mesh_dump(cl);
    } else if (!strcmp(c, "self")) {
      void *s = ((void *(*)(void *, int))VSLOT(tm, 50))(tm, 0);
      printf("self %d\n", s == (void *)tm);
    } else {
      printf("ERROR unknown command %s\n", c);
      exit(2);
    }
  }
}

/* Deterministic per-triangle materials / foreign ids for the "aux" option. */
static signed char *gen_mats(int nt) {
  signed char *m = (signed char *)malloc(nt + 1);
  for (int i = 0; i < nt; i++) m[i] = (signed char)((i * 37 + 5) % 23 - 7);
  return m;
}
static int *gen_fids(int nt) {
  int *f = (int *)malloc(4 * (nt + 1));
  for (int i = 0; i < nt; i++) f[i] = ((i * 13 + 3) % 17) | ((i % 3 == 2) ? 0x08000000 : 0);
  return f;
}

int main(int argc, char **argv) {
  p1c_init(&argc, &argv);
  HMODULE xl = GetModuleHandleA("xlcommon.dll");
  if (!xl) { printf("ERROR xlcommon not loaded\n"); return 1; }
  g_seed = (fn_seed)GetProcAddress(xl, "?CryRandomSeed@@YAXI@Z");
  if (!g_seed) { printf("ERROR CryRandomSeed\n"); return 1; }
  if (argc >= 8 && !strcmp(argv[1], "mesh")) {
    long len;
    unsigned char *b = p1c_read_file(argv[2], &len);
    int nv = *(int *)b, nt = *(int *)(b + 4);
    float *v = (float *)(b + 8);
    int *idx = (int *)(b + 8 + 12 * nv);
    int flags = (int)strtoul(argv[3], NULL, 16);
    int aux = atoi(argv[7]);
    unsigned char *tm = p1c_mesh_new(v, idx, 4, (aux & 1) ? gen_mats(nt) : NULL, (aux & 2) ? gen_fids(nt) : NULL, nt,
                                     flags, atoi(argv[4]), atoi(argv[5]), bf((uint32_t)strtoul(argv[6], NULL, 16)));
    p1c_mesh_dump(tm);
    Toks t = {argv + 8, argc - 8, 0};
    run(tm, &t);
    return 0;
  }
  if (argc >= 3 && !strcmp(argv[1], "blobs")) {
    long len;
    unsigned char *b = p1c_read_file(argv[2], &len);
    long o = 0;
    int k = 0;
    while (o + 4 <= len) {
      int bl = *(int *)(b + o);
      unsigned char *blob = b + o + 4;
      o += 4 + bl;
      int cl = *(int *)(b + o);
      char *cmd = (char *)malloc(cl + 1);
      memcpy(cmd, b + o + 4, cl);
      cmd[cl] = 0;
      o += 4 + cl;
      printf("case %d\n", k++);
      P1cStream *s = p1c_stream_read(blob, bl);
      s->pos = 4 + 64 + 4;
      unsigned char *tm = p1c_mesh_load(s, NULL, NULL, NULL);
      p1c_mesh_dump(tm);
      char *toks[4096];
      int nt = 0;
      char *p = cmd;
      char buf[64];
      while (nt < 4096 && next_tok(&p, buf, sizeof buf)) toks[nt++] = _strdup(buf);
      Toks t = {toks, nt, 0};
      run(tm, &t);
      fflush(stdout);
    }
    return 0;
  }
  printf("usage: mesh <file> <flags> <minTris> <maxTris> <obbHex> <aux> [cmds] | blobs <file>\n");
  return 1;
}
