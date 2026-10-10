/* p1c_core_probe: the CTriMesh constructor, topology, islands, IsConvex, Save and Load run
 * *inside* the real x64 cryphysics_dedicate.dll (10.0.2.13); everything is printed as raw bits.
 * Oracle of tests/p1c_core.rs (P1c-core). The reference binaries are only loaded.
 *
 * Build:  tests\p1c_build_probe.bat <abs path of this file> <out.exe>
 * Run:    p1c_core_probe [--rt DIR] CMD ...
 *   ctor MESHFILE flags minTris maxTris obbFactorBits idxmode extras
 *        CTriMesh ctor 0x351BF470 + constructor 0x351CF1B0 on the mesh file (u32 nVerts, u32 nTris,
 *        f32 verts[3n], i32 idx[3t]); idxmode u16 | i32 (stride 2 / 4); extras: letters m
 *        (materials (t*7 % 11) - 5), f (foreign ids nt-1-t), or '-'. Prints the mesh dump,
 *        the bad-edge count of sub_351BFA60(..., 1), a sequence of IsConvex calls with the cache,
 *        the Save bytes (slot 25) and the dump of a fresh mesh loaded (slot 26) from them.
 *   blobs BATCHFILE  every phys-geometry blob of the batch: u32 count, then per blob
 *        u32 len, bytes, u32 nPos, f32 pos[3 nPos], u32 nIdx, u16 idx[nIdx], u32 nMats,
 *        u8 mats[nMats]. A trimesh blob is loaded through slot 26 (version, 64-byte header and type
 *        id read as LoadPhysGeometry / LoadGeometry do; the render arrays are passed as pVtx
 *        (stride 12), pIdx (stride 2), pMats, or NULL when empty), dumped, and saved (slot 25). */
#include "p1c_common.h"

static void dump_convex_seq(unsigned char *tm) {
  static const float tols[] = {0.02f, 0.1f, 0.0f, 0.5f, 0.02f, 1.0f, 0.1f, 0.25f, 0.3f, 0.0f};
  typedef int (*fn_conv)(void *, float);
  printf("convex");
  for (int i = 0; i < (int)(sizeof tols / sizeof tols[0]); i++) {
    int r = ((fn_conv)VSLOT(tm, 21))(tm, tols[i]);
    printf(" %d", r);
  }
  printf("\n");
  prw("convcache", tm + 480, 8);
}

static void save_mesh(unsigned char *tm, const char *tag, P1cStream **out) {
  P1cStream *w = p1c_stream_write(0);
  ((void (*)(void *, P1cStream *))VSLOT(tm, 25))(tm, w);
  p1c_print_bytes(tag, w->buf, w->pos);
  if (out) *out = w;
}

static int cmd_ctor(int argc, char **argv) {
  if (argc < 9) { printf("usage: ctor MESH flags min max obbbits idxmode extras\n"); return 2; }
  long len;
  unsigned char *f = p1c_read_file(argv[2], &len);
  int nv = *(int *)f, nt = *(int *)(f + 4);
  float *v = (float *)calloc(3 * (size_t)nv + 3, 4);
  int *idx = (int *)calloc(3 * (size_t)nt + 3, 4);
  memcpy(v, f + 8, 12 * (size_t)nv);
  memcpy(idx, f + 8 + 12 * (size_t)nv, 12 * (size_t)nt);
  unsigned flags = (unsigned)strtoul(argv[3], NULL, 0);
  int mn = atoi(argv[4]), mx = atoi(argv[5]);
  float obb = bf((uint32_t)strtoul(argv[6], NULL, 16));
  int i32 = !strcmp(argv[7], "i32");
  const char *ex = argv[8];
  void *ip = idx;
  int stride = 4;
  if (!i32) {
    uint16_t *s = (uint16_t *)calloc(3 * (size_t)nt + 3, 2);
    for (int i = 0; i < 3 * nt; i++) s[i] = (uint16_t)idx[i];
    ip = s;
    stride = 2;
  }
  signed char *mats = NULL;
  int *fid = NULL;
  if (strchr(ex, 'm')) {
    mats = (signed char *)calloc(nt + 1, 1);
    for (int t = 0; t < nt; t++) mats[t] = (signed char)((t * 7) % 11 - 5);
  }
  if (strchr(ex, 'f')) {
    fid = (int *)calloc(nt + 1, 4);
    for (int t = 0; t < nt; t++) fid[t] = nt - 1 - t;
  }
  unsigned char *tm = p1c_mesh_new(v, ip, stride, mats, fid, nt, (int)flags, mn, mx, obb);
  p1c_mesh_dump(tm);
  typedef int (*fn_topo)(void *, void *, int);
  int bad = ((fn_topo)VA(0x351BFA60ULL))(tm, *(void **)(tm + 56), 1);
  printf("badedges %d\n", bad);
  P1cStream *w;
  save_mesh(tm, "save", &w);
  /* Load the saved bytes into a fresh mesh (vertices/indices from the stream). */
  P1cStream *r = p1c_stream_read(w->buf, w->pos);
  P1cStrided pv = {v, 12}, pi = {ip, stride};
  unsigned char *tm2 = p1c_mesh_load(r, &pv, &pi, mats);
  printf("loaded %d\n", r->pos);
  p1c_mesh_dump(tm2);
  save_mesh(tm2, "resave", NULL);
  dump_convex_seq(tm);
  return 0;
}

static int cmd_blobs(int argc, char **argv) {
  if (argc < 3) { printf("usage: blobs BATCH\n"); return 2; }
  long len;
  unsigned char *f = p1c_read_file(argv[2], &len);
  unsigned char *p = f;
  unsigned n = *(unsigned *)p;
  p += 4;
  for (unsigned b = 0; b < n; b++) {
    unsigned bl = *(unsigned *)p;
    p += 4;
    unsigned char *blob = p;
    p += bl;
    unsigned npos = *(unsigned *)p;
    p += 4;
    float *pos = (float *)p;
    p += 12 * (size_t)npos;
    unsigned nidx = *(unsigned *)p;
    p += 4;
    uint16_t *idx = (uint16_t *)p;
    p += 2 * (size_t)nidx;
    unsigned nmats = *(unsigned *)p;
    p += 4;
    unsigned char *mats = p;
    p += nmats;
    P1cStream *s = p1c_stream_read(blob, (int)bl);
    int ver = *(int *)(s->buf + s->pos);
    s->pos += 4;
    if (ver != 1) { printf("blob %u version %d\n", b, ver); continue; }
    s->pos += 64;
    int type = *(int *)(s->buf + s->pos);
    s->pos += 4;
    if (type != 1) { printf("blob %u type %d\n", b, type); continue; }
    P1cStrided pv = {npos ? pos : NULL, 12}, pi = {nidx ? idx : NULL, 2};
    unsigned char *tm = p1c_mesh_load(s, &pv, &pi, nmats ? mats : NULL);
    printf("blob %u read %d\n", b, s->pos);
    p1c_mesh_dump(tm);
    save_mesh(tm, "save", NULL);
  }
  return 0;
}

int main(int argc, char **argv) {
  setvbuf(stdout, NULL, _IOFBF, 1 << 20);
  p1c_init(&argc, &argv);
  if (argc < 2) { printf("usage: p1c_core_probe CMD ...\n"); return 2; }
  if (!strcmp(argv[1], "ctor")) return cmd_ctor(argc, argv);
  if (!strcmp(argv[1], "blobs")) return cmd_blobs(argc, argv);
  printf("unknown command %s\n", argv[1]);
  return 2;
}
