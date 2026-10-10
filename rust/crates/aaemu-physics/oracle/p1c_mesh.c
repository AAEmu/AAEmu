/* p1c_mesh.c: trimesh geometry for physoracle (P1c-oracle). #included by physoracle.c.
 *
 *   geom name=N type=mesh <source> [flags=0x40] [mintris=2] [maxtris=4] [obb=1] [tol=0]
 *                                  [mat=-1] [scale=1] [offset=x,y,z] [dump=1]
 *   geominfo name=N                 (summary line of any registered geometry)
 *
 * <source> is one of
 *   file=PATH                 mesh file relative to the scenario's directory:
 *                             u32 nVerts, u32 nTris, f32 verts[3*nVerts], i32 idx[3*nTris] (LE)
 *   gen=grid|box|icosphere|cylinder|terrainmesh ...   built-in generators (see p1c_gen_*)
 *   cgf=REL [slot=k]          the k-th MeshPhysicsData chunk (0xCCCC0018, chunk-table order) of
 *                             %AAEMU_PHYS_CGF_DIR%\REL, loaded by IGeomManager::LoadPhysGeometry
 *                             (slot 16, 0x3514B340) exactly as the game's CStatObj does.
 *
 * file= and gen= meshes go through IGeomManager slot 4 (0x35095CB0) CreateMesh(strided vtx,
 * strided u16 idx, mats, foreignIdx, nTris, flags, approx_tolerance, nMinTrisPerNode,
 * nMaxTrisPerNode, favorAABB), then slot 9 RegisterGeometry. Generators compute every vertex in
 * single precision (sin/cos: double Taylor series, see p1c_sin) so a port can rebuild the same
 * bits; dump=1 prints the vertices and indices the engine stored anyway.
 */

/* IGeomManager slots used here (vtable 0x351F09E8 inside the world, x64) */
enum { G_CreateMeshEx = 4, G_LoadPhysGeometry = 16 };

typedef struct { void *p; int stride; int pad; } P1cStr;
typedef void *(*fn_createmesh)(void *gm, P1cStr *v, P1cStr *i, signed char *mats, int *foreign, int nTris, int flags,
                               float tol, int minTris, int maxTris, float favorAABB);
typedef void *(*fn_loadpg)(void *gm, void *stream, P1cStr *v, P1cStr *i, signed char *mats);
typedef void (*fn_gbbox)(void *g, float *box);
typedef float (*fn_gvol)(void *g);
typedef int (*fn_gint)(void *g);

static char g_scn_dir[MAX_PATH];
static void p1c_set_scenario_path(const char *path) {
  g_scn_dir[0] = 0;
  if (!strcmp(path, "-")) return;
  strncpy(g_scn_dir, path, sizeof g_scn_dir - 1);
  char *s = strrchr(g_scn_dir, '\\'), *s2 = strrchr(g_scn_dir, '/');
  if (s2 > s) s = s2;
  if (s) s[1] = 0; else g_scn_dir[0] = 0;
}

/* ------------------------------------------------------------------ mesh builder */
typedef struct { float *v; int nv, cv; int *idx; int nt, ct; } P1cMesh;
static int pm_vert(P1cMesh *m, float x, float y, float z) {
  if (m->nv == m->cv) { m->cv = m->cv ? 2 * m->cv : 64; m->v = (float *)realloc(m->v, 12 * (size_t)m->cv); }
  m->v[3 * m->nv] = x; m->v[3 * m->nv + 1] = y; m->v[3 * m->nv + 2] = z;
  return m->nv++;
}
static void pm_tri(P1cMesh *m, int a, int b, int c) {
  if (m->nt == m->ct) { m->ct = m->ct ? 2 * m->ct : 64; m->idx = (int *)realloc(m->idx, 12 * (size_t)m->ct); }
  m->idx[3 * m->nt] = a; m->idx[3 * m->nt + 1] = b; m->idx[3 * m->nt + 2] = c;
  m->nt++;
}

/* sin/cos of a double in [-pi, pi]: Taylor series to x^25, evaluated term by term in double
   (term_k = term_{k-1} * -x^2 / ((2k)(2k+1)), summed in order). Reproducible with plain IEEE ops. */
static double p1c_sin(double x) {
  double x2 = x * x, term = x, sum = x;
  for (int k = 1; k <= 12; k++) { term = term * -x2 / (double)((2 * k) * (2 * k + 1)); sum += term; }
  return sum;
}
static double p1c_cos(double x) {
  double x2 = x * x, term = 1.0, sum = 1.0;
  for (int k = 1; k <= 12; k++) { term = term * -x2 / (double)((2 * k - 1) * (2 * k)); sum += term; }
  return sum;
}
/* reduce to [-pi, pi] by whole turns (double) */
static double p1c_wrap(double a) {
  const double tau = 6.283185307179586;
  while (a > 3.141592653589793) a -= tau;
  while (a < -3.141592653589793) a += tau;
  return a;
}
/* noise in [-amp, amp]: splitmix64 finaliser of seed ^ (i << 32) ^ j, top 24 bits */
static float p1c_noise(uint64_t seed, int i, int j, float amp) {
  uint64_t z = (seed ^ ((uint64_t)(uint32_t)i << 32) ^ (uint64_t)(uint32_t)j) + 0x9E3779B97F4A7C15ULL;
  z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
  z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
  z ^= z >> 31;
  float u = (float)(z >> 40) * (1.0f / 16777216.0f);
  return amp * (2.0f * u - 1.0f);
}

/* gen=grid nx= ny= step=s|sx,sy [z=0] [noise=seed,amp]
   gen=terrainmesh nx= ny= step= [z=0] amp= [k=kx,ky (0.5,0.5)]
   Vertex (i, j), i = 0..nx, j = 0..ny, index j*(nx+1)+i:
     x = sx * ((float)i - 0.5f*(float)nx),  y = sy * ((float)j - 0.5f*(float)ny)
     grid:        z = z0 + noise(seed, i, j, amp)                (0 without noise=)
     terrainmesh: z = z0 + amp * (float)(sin(kx*x) * cos(ky*y))  (kx*x, ky*y in float, then double)
   Cell (i, j): a=(i,j) b=(i+1,j) c=(i+1,j+1) d=(i,j+1) -> triangles (a,b,c), (a,c,d) (normals +z). */
static void p1c_gen_grid(const Toks *t, P1cMesh *m, int terrain) {
  int nx = atoi(need(t, "nx")), ny = atoi(need(t, "ny"));
  if (nx < 1 || ny < 1) die("grid needs nx, ny >= 1", 0);
  float st[2] = {1, 1};
  int ns = parse_fv(need(t, "step"), st, 2);
  if (ns == 1) st[1] = st[0];
  float z0 = parse_f(get(t, "z", "0"));
  float amp = 0, k[2] = {0.5f, 0.5f};
  uint64_t seed = 0;
  int noisy = 0;
  if (terrain) {
    amp = parse_f(need(t, "amp"));
    const char *ks = get(t, "k", 0);
    if (ks && parse_fv(ks, k, 2) == 1) k[1] = k[0];
  } else if (get(t, "noise", 0)) {
    char buf[128];
    strncpy(buf, get(t, "noise", 0), sizeof buf - 1);
    buf[sizeof buf - 1] = 0;
    char *c = strchr(buf, ',');
    if (!c) die("noise=seed,amp", buf);
    *c = 0;
    seed = strtoull(buf, 0, 0);
    amp = parse_f(c + 1);
    noisy = 1;
  }
  for (int j = 0; j <= ny; j++)
    for (int i = 0; i <= nx; i++) {
      float x = st[0] * ((float)i - 0.5f * (float)nx), y = st[1] * ((float)j - 0.5f * (float)ny), z = z0;
      if (terrain) {
        float ax = k[0] * x, ay = k[1] * y;
        z = z0 + amp * (float)(p1c_sin(p1c_wrap((double)ax)) * p1c_cos(p1c_wrap((double)ay)));
      } else if (noisy) {
        z = z0 + p1c_noise(seed, i, j, amp);
      }
      pm_vert(m, x, y, z);
    }
  for (int j = 0; j < ny; j++)
    for (int i = 0; i < nx; i++) {
      int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 2, d = a + nx + 1;
      pm_tri(m, a, b, c);
      pm_tri(m, a, c, d);
    }
}

/* gen=box size=hx,hy,hz: vertex k = (bit0 ? +hx : -hx, bit1 ? +hy : -hy, bit2 ? +hz : -hz),
   12 outward (counter-clockwise seen from outside) triangles in the order -z, +z, -y, +y, -x, +x. */
static void p1c_gen_box(const Toks *t, P1cMesh *m) {
  float h[3];
  if (parse_fv(need(t, "size"), h, 3) != 3) die("box needs size=hx,hy,hz", 0);
  for (int k = 0; k < 8; k++) pm_vert(m, (k & 1) ? h[0] : -h[0], (k & 2) ? h[1] : -h[1], (k & 4) ? h[2] : -h[2]);
  static const int f[12][3] = {{0, 2, 3}, {0, 3, 1}, {4, 5, 7}, {4, 7, 6}, {0, 1, 5}, {0, 5, 4},
                               {2, 6, 7}, {2, 7, 3}, {0, 4, 6}, {0, 6, 2}, {1, 3, 7}, {1, 7, 5}};
  for (int i = 0; i < 12; i++) pm_tri(m, f[i][0], f[i][1], f[i][2]);
}

/* unit-normalise vertex i: n2 = (x*x + y*y) + z*z, inv = 1.0f / sqrtf(n2), v *= inv */
static void pm_unit(P1cMesh *m, int i) {
  float *v = m->v + 3 * i;
  float n2 = (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2];
  float inv = 1.0f / sqrtf(n2);
  v[0] *= inv; v[1] *= inv; v[2] *= inv;
}
/* gen=icosphere r= [sub=1]: the 12-vertex icosahedron (+-1, +-t, 0), (0, +-1, +-t), (+-t, 0, +-1) with
   t = 1.618034f, each vertex made unit; sub times every triangle (a,b,c) becomes (a,ab,ca),
   (b,bc,ab), (c,ca,bc), (ab,bc,ca) with shared edge midpoints ((a+b)*0.5f, then made unit; new
   vertices appended in the order they are first needed); finally every vertex is scaled by r. */
static void p1c_gen_icosphere(const Toks *t, P1cMesh *m) {
  float r = parse_f(need(t, "r"));
  int sub = atoi(get(t, "sub", "1"));
  const float tt = 1.618034f;
  const float base[12][3] = {{-1, tt, 0}, {1, tt, 0}, {-1, -tt, 0}, {1, -tt, 0}, {0, -1, tt}, {0, 1, tt},
                             {0, -1, -tt}, {0, 1, -tt}, {tt, 0, -1}, {tt, 0, 1}, {-tt, 0, -1}, {-tt, 0, 1}};
  static const int f[20][3] = {{0, 11, 5}, {0, 5, 1}, {0, 1, 7}, {0, 7, 10}, {0, 10, 11}, {1, 5, 9}, {5, 11, 4},
                               {11, 10, 2}, {10, 7, 6}, {7, 1, 8}, {3, 9, 4}, {3, 4, 2}, {3, 2, 6}, {3, 6, 8},
                               {3, 8, 9}, {4, 9, 5}, {2, 4, 11}, {6, 2, 10}, {8, 6, 7}, {9, 8, 1}};
  for (int i = 0; i < 12; i++) { pm_vert(m, base[i][0], base[i][1], base[i][2]); pm_unit(m, i); }
  for (int i = 0; i < 20; i++) pm_tri(m, f[i][0], f[i][1], f[i][2]);
  for (int s = 0; s < sub; s++) {
    int nt = m->nt;
    int *old = (int *)malloc(12 * (size_t)nt);
    memcpy(old, m->idx, 12 * (size_t)nt);
    int *ea = (int *)malloc(4 * 3 * (size_t)nt), *eb = (int *)malloc(4 * 3 * (size_t)nt), *em = (int *)malloc(4 * 3 * (size_t)nt);
    int ne = 0;
    m->nt = 0;
    for (int i = 0; i < nt; i++) {
      int mid[3];
      for (int e = 0; e < 3; e++) {
        int a = old[3 * i + e], b = old[3 * i + (e + 1) % 3], lo = a < b ? a : b, hi = a < b ? b : a, found = -1;
        for (int k = 0; k < ne; k++)
          if (ea[k] == lo && eb[k] == hi) { found = em[k]; break; }
        if (found < 0) {
          const float *va = m->v + 3 * a, *vb = m->v + 3 * b;
          found = pm_vert(m, (va[0] + vb[0]) * 0.5f, (va[1] + vb[1]) * 0.5f, (va[2] + vb[2]) * 0.5f);
          pm_unit(m, found);
          ea[ne] = lo; eb[ne] = hi; em[ne] = found; ne++;
        }
        mid[e] = found; /* mid[0] = ab, mid[1] = bc, mid[2] = ca */
      }
      int a = old[3 * i], b = old[3 * i + 1], c = old[3 * i + 2];
      pm_tri(m, a, mid[0], mid[2]);
      pm_tri(m, b, mid[1], mid[0]);
      pm_tri(m, c, mid[2], mid[1]);
      pm_tri(m, mid[0], mid[1], mid[2]);
    }
    free(old); free(ea); free(eb); free(em);
  }
  for (int i = 0; i < 3 * m->nv; i++) m->v[i] *= r;
}

/* gen=cylinder r= hh= [seg=16]: axis z. Ring vertex k (k = 0..seg-1) at angle
   a = wrap((double)k * (2*pi / (double)seg)): bottom b_k = (r*(float)cos a, r*(float)sin a, -hh) is
   vertex k, top t_k (same x, y, +hh) is vertex seg+k, then the bottom centre (0,0,-hh) = 2*seg and
   the top centre (0,0,hh) = 2*seg+1. Per k (k1 = (k+1)%seg): side (b_k,b_k1,t_k1), (b_k,t_k1,t_k),
   bottom (cb,b_k1,b_k), top (ct,t_k,t_k1). */
static void p1c_gen_cylinder(const Toks *t, P1cMesh *m) {
  float r = parse_f(need(t, "r")), hh = parse_f(need(t, "hh"));
  int seg = atoi(get(t, "seg", "16"));
  if (seg < 3) die("cylinder needs seg >= 3", 0);
  float cs[2][1024];
  if (seg > 1024) die("cylinder seg > 1024", 0);
  for (int k = 0; k < seg; k++) {
    double a = p1c_wrap((double)k * (6.283185307179586 / (double)seg));
    cs[0][k] = (float)p1c_cos(a);
    cs[1][k] = (float)p1c_sin(a);
  }
  for (int z = 0; z < 2; z++)
    for (int k = 0; k < seg; k++) pm_vert(m, r * cs[0][k], r * cs[1][k], z ? hh : -hh);
  int cb = pm_vert(m, 0, 0, -hh), ct = pm_vert(m, 0, 0, hh);
  for (int k = 0; k < seg; k++) {
    int k1 = (k + 1) % seg;
    pm_tri(m, k, k1, seg + k1);
    pm_tri(m, k, seg + k1, seg + k);
    pm_tri(m, cb, k1, k);
    pm_tri(m, ct, seg + k, seg + k1);
  }
}

static void p1c_read_meshfile(const char *rel, P1cMesh *m) {
  char path[MAX_PATH * 2];
  if (rel[0] && (rel[1] == ':' || rel[0] == '\\' || rel[0] == '/')) snprintf(path, sizeof path, "%s", rel);
  else snprintf(path, sizeof path, "%s%s", g_scn_dir, rel);
  FILE *f = fopen(path, "rb");
  if (!f) die("cannot open mesh file", path);
  uint32_t h[2];
  if (fread(h, 4, 2, f) != 2) die("short mesh file", path);
  if (h[0] == 0 || h[0] > 65536 || h[1] == 0 || h[1] > (1u << 22)) die("bad mesh file header", path);
  m->nv = m->cv = (int)h[0];
  m->nt = m->ct = (int)h[1];
  m->v = (float *)malloc(12 * (size_t)m->nv);
  m->idx = (int *)malloc(12 * (size_t)m->nt);
  if (fread(m->v, 12, m->nv, f) != (size_t)m->nv || fread(m->idx, 12, m->nt, f) != (size_t)m->nt)
    die("short mesh file", path);
  fclose(f);
}

/* ------------------------------------------------------------------ CMemStream (for LoadPhysGeometry)
   +0 vtable {dtor, grow}, +8 buffer, +20 read/write position, +24 capacity, +28 owns buffer */
typedef struct { void **vt; unsigned char *buf; int pad16; int pos; int cap; unsigned char own; unsigned char pad[3]; int measure; int pad36; } P1cMemStream;
static void p1c_ms_dtor(void *self, char del) { (void)self; (void)del; }
static void p1c_ms_grow(P1cMemStream *s, int n) { (void)s; (void)n; die("CMemStream grow on a read stream", 0); }
static void *g_p1c_ms_vt[2] = {(void *)p1c_ms_dtor, (void *)p1c_ms_grow};

static uint32_t p1c_u32(const unsigned char *b, size_t o) { uint32_t u; memcpy(&u, b + o, 4); return u; }

/* the k-th 0xCCCC0018 chunk's blob (chunk + 40, size at chunk + 16) */
static unsigned char *p1c_cgf_blob(const char *rel, int k, int *len) {
  const char *dir = getenv("AAEMU_PHYS_CGF_DIR");
  if (!dir) die("cgf= needs AAEMU_PHYS_CGF_DIR", rel);
  char path[MAX_PATH * 2];
  snprintf(path, sizeof path, "%s\\%s", dir, rel);
  FILE *f = fopen(path, "rb");
  if (!f) die("cannot open cgf", path);
  fseek(f, 0, SEEK_END);
  long n = ftell(f);
  fseek(f, 0, SEEK_SET);
  unsigned char *b = (unsigned char *)malloc((size_t)n + 64);
  if (fread(b, 1, (size_t)n, f) != (size_t)n) die("short cgf", path);
  fclose(f);
  if (n < 20 || memcmp(b, "CryTek", 6)) die("not a CryTek file", path);
  uint32_t ver = p1c_u32(b, 12), to = p1c_u32(b, 16), nch = p1c_u32(b, to), es = ver == 0x744 ? 16 : 20;
  int seen = 0;
  for (uint32_t i = 0; i < nch; i++) {
    size_t e = to + 4 + (size_t)i * es;
    if (e + 16 > (size_t)n) break;
    if (p1c_u32(b, e) != 0xCCCC0018u) continue;
    if (seen++ != k) continue;
    uint32_t o = p1c_u32(b, e + 8), size = p1c_u32(b, o + 16);
    if ((size_t)o + 40 + size > (size_t)n) die("truncated physics chunk", path);
    unsigned char *blob = (unsigned char *)malloc((size_t)size + 64);
    memcpy(blob, b + o + 40, size);
    memset(blob + size, 0, 64);
    free(b);
    *len = (int)size;
    return blob;
  }
  die("no such physics chunk in", path);
  return 0;
}

/* the blob's trimesh part refers to the render mesh's vertices (hasForeignIdx without 0x400000)? */
static int p1c_blob_shared(const unsigned char *b, int len) {
  if (len < 72 + 18 || p1c_u32(b, 0) != 1 || p1c_u32(b, 68) != 1) return 0;
  uint32_t nv = p1c_u32(b, 72), flags = p1c_u32(b, 84);
  size_t r = 88;
  if (b[r++]) r += 2 * (size_t)nv;
  if (r >= (size_t)len) return 0;
  return b[r] && !(flags & 0x400000);
}

/* ------------------------------------------------------------------ geominfo */
/* geominfo NAME type T nprims N nv N flags F bbox <16 words> vol V pg <V> <Ibody 3> <q 4> <origin 3> <surface>
   bbox = GetBBox (slot 6) as primitives::box {Basis 9, bOriented, center 3, size 3}; vol = GetVolume
   (slot 31); pg = the phys_geometry fields (+48 V, +8 Ibody, +20 q, +36 origin, +56 surface_idx).
   nv and flags are the CTriMesh fields +124 / +152 (0 for other types). */
static void p1c_geominfo(const char *name, void *pg) {
  void *g = *(void **)pg;
  int type = ((fn_gint)VT(g)[1])(g);
  int np = ((fn_gint)VT(g)[28])(g);
  __declspec(align(16)) float box[32];
  memset(box, 0, sizeof box);
  ((fn_gbbox)VT(g)[6])(g, box);
  float vol = ((fn_gvol)VT(g)[31])(g);
  int nv = 0;
  uint32_t flags = 0;
  if (type == 1) { nv = *(int *)((char *)g + 124); flags = *(uint32_t *)((char *)g + 152); }
  printf("geominfo %s type %d nprims %d nv %d flags %08x bbox", name, type, np, nv, flags);
  for (int i = 0; i < 16; i++) printf(" %08x", ((uint32_t *)box)[i]);
  printf(" vol %08x pg %08x", fbits(vol), *(uint32_t *)((char *)pg + 48));
  out_f((float *)((char *)pg + 8), 3);
  out_f((float *)((char *)pg + 20), 4);
  out_f((float *)((char *)pg + 36), 3);
  printf(" %d\n", *(int *)((char *)pg + 56));
}

/* meshv NAME <3*nv words> / mesht NAME <3*nt ints> [/ meshf NAME <nt ints>]: what the engine stored
   (CTriMesh +80/+88 vertices, +56 indices, +72 foreign ids when present) */
static void p1c_dump_mesh(const char *name, void *pg) {
  char *g = *(char **)pg;
  if (((fn_gint)VT(g)[1])(g) != 1) return;
  int nt = *(int *)(g + 120), nv = *(int *)(g + 124), stride = *(int *)(g + 88);
  char *v = *(char **)(g + 80);
  int *idx = *(int **)(g + 56);
  printf("meshv %s", name);
  for (int i = 0; i < nv; i++) out_f((float *)(v + (size_t)i * stride), 3);
  printf("\nmesht %s", name);
  for (int i = 0; i < 3 * nt; i++) printf(" %d", idx[i]);
  int *fid = *(int **)(g + 72); /* pForeignIdx (per stored triangle), if any */
  if (fid) {
    printf("\nmeshf %s", name);
    for (int i = 0; i < nt; i++) printf(" %d", fid[i]);
  }
  printf("\n");
}

static void *p1c_geom_mesh(const Toks *t) {
  const char *name = need(t, "name");
  void *pg = 0;
  if (get(t, "cgf", 0)) {
    int len = 0;
    unsigned char *blob = p1c_cgf_blob(get(t, "cgf", 0), atoi(get(t, "slot", "0")), &len);
    if (p1c_blob_shared(blob, len)) die("cgf blob uses the render mesh's vertices (not supported)", get(t, "cgf", 0));
    P1cMemStream *s = (P1cMemStream *)calloc(1, sizeof *s);
    s->vt = g_p1c_ms_vt;
    s->buf = blob;
    s->cap = len;
    P1cStr sv = {0, 12, 0}, si = {0, 2, 0};
    pg = ((fn_loadpg)VT(g_gm)[G_LoadPhysGeometry])(g_gm, s, &sv, &si, 0);
    if (!pg) die("LoadPhysGeometry failed", get(t, "cgf", 0));
    printf("cgfload %s used %d of %d\n", name, s->pos, len);
  } else {
    P1cMesh m;
    memset(&m, 0, sizeof m);
    const char *gen = get(t, "gen", 0), *file = get(t, "file", 0);
    if (file) p1c_read_meshfile(file, &m);
    else if (!gen) die("type=mesh needs file=, gen= or cgf=", name);
    else if (!strcmp(gen, "grid")) p1c_gen_grid(t, &m, 0);
    else if (!strcmp(gen, "terrainmesh")) p1c_gen_grid(t, &m, 1);
    else if (!strcmp(gen, "box")) p1c_gen_box(t, &m);
    else if (!strcmp(gen, "icosphere")) p1c_gen_icosphere(t, &m);
    else if (!strcmp(gen, "cylinder")) p1c_gen_cylinder(t, &m);
    else die("unknown mesh generator", gen);
    /* optional scale and offset, applied per vertex as v*scale + offset (float) */
    float sc = parse_f(get(t, "scale", "1")), off[3] = {0, 0, 0};
    const char *os = get(t, "offset", 0);
    if (os) parse_fv(os, off, 3);
    if (sc != 1.0f || off[0] != 0 || off[1] != 0 || off[2] != 0)
      for (int i = 0; i < m.nv; i++)
        for (int k = 0; k < 3; k++) m.v[3 * i + k] = m.v[3 * i + k] * sc + off[k];
    if (m.nv > 65536) die("mesh has more than 65536 vertices (u16 indices)", name);
    uint16_t *i16 = (uint16_t *)malloc(6 * (size_t)m.nt);
    for (int i = 0; i < 3 * m.nt; i++) {
      if (m.idx[i] < 0 || m.idx[i] >= m.nv) die("mesh index out of range", name);
      i16[i] = (uint16_t)m.idx[i];
    }
    signed char *mats = 0;
    const char *ms = get(t, "mat", 0);
    if (ms) { mats = (signed char *)malloc((size_t)m.nt); memset(mats, atoi(ms), (size_t)m.nt); }
    P1cStr sv = {m.v, 12, 0}, si = {i16, 2, 0};
    /* the vertex/index arrays are copied by the CTriMesh constructor unless flags 1/2 are passed;
       they are kept alive (never freed) so those flags work too */
    void *g = ((fn_createmesh)VT(g_gm)[G_CreateMeshEx])(
        g_gm, &sv, &si, mats, 0, m.nt, (int)parse_u(get(t, "flags", "0x40")), parse_f(get(t, "tol", "0")),
        atoi(get(t, "mintris", "2")), atoi(get(t, "maxtris", "4")), parse_f(get(t, "obb", "1")));
    if (!g) die("CreateMesh failed", name);
    pg = ((fn_reg)VT(g_gm)[G_RegisterGeometry])(g_gm, g, 0, 0, 0);
  }
  p1c_geominfo(name, pg);
  if (atoi(get(t, "dump", "0"))) p1c_dump_mesh(name, pg);
  return pg;
}
