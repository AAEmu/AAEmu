/* P2b probe extension hook for the "rot_box" work group (owned by that group; see p2b_probe.c).
 * Handle internal-routine probe commands here and return 1; return 0 for lines it does not own.
 *
 * Shared swept/common.rs helpers, all words hex (floats as raw bits); answer `rbx <words>`:
 *   RBX_ROTV  v[3] axis[3] c s        sub_35020EA0(v, dst, axis, c, s)          -> dst[3]
 *   RBX_ROTP  pt[3] ctr[3] axis[3] c s sub_3519A670(pt, dst, ctr, axis, c, s)   -> dst[3]
 *   RBX_NORMI v[3]                    sub_35021020(v) in place                  -> v[3]
 *   RBX_NORM  v[3]                    sub_350210A0(v, dst)                       -> dst[3] v[3]
 *   RBX_MATMUL a[9] b[9]              sub_35022380(dst, a, b) (distinct buffers) -> dst[9]
 */
typedef float *(*p2b_rbx_rotv_fn)(const float *v, float *dst, const float *axis, float c, float s);
typedef float *(*p2b_rbx_rotp_fn)(const float *pt, float *dst, const float *ctr, const float *axis, float c,
                                  float s);
typedef float *(*p2b_rbx_normi_fn)(float *v);
typedef float *(*p2b_rbx_norm_fn)(const float *v, float *dst);
typedef float *(*p2b_rbx_matmul_fn)(float *dst, const float *a, const float *b);

static void p2b_rbx_floats(char **cur, float *f, int n) {
  for (int i = 0; i < n; i++) f[i] = bitsf(p2b_word(cur));
}
static void p2b_rbx_print(const float *f, int n) {
  for (int i = 0; i < n; i++) printf(" %08x", fbits(f[i]));
}

static int p2b_ext_rot_box(char *line) {
  char *cur = line;
  while (*cur == ' ' || *cur == '\t') cur++;
  if (strncmp(cur, "RBX_", 4)) return 0; /* not ours: leave the line untouched for the other hooks */
  char *cmd = p2b_tok(&cur);
  __declspec(align(16)) float in[32], dst[16];
  memset(in, 0, sizeof in);
  for (int i = 0; i < 16; i++) dst[i] = bitsf(0xDEAD0000u + i);
  printf("rbx");
  if (!strcmp(cmd, "RBX_ROTV")) {
    p2b_rbx_floats(&cur, in, 8);
    ((p2b_rbx_rotv_fn)p2b_fn(0x35020EA0ULL))(in, dst, in + 3, in[6], in[7]);
    p2b_rbx_print(dst, 3);
  } else if (!strcmp(cmd, "RBX_ROTP")) {
    p2b_rbx_floats(&cur, in, 11);
    ((p2b_rbx_rotp_fn)p2b_fn(0x3519A670ULL))(in, dst, in + 3, in + 6, in[9], in[10]);
    p2b_rbx_print(dst, 3);
  } else if (!strcmp(cmd, "RBX_NORMI")) {
    p2b_rbx_floats(&cur, in, 3);
    ((p2b_rbx_normi_fn)p2b_fn(0x35021020ULL))(in);
    p2b_rbx_print(in, 3);
  } else if (!strcmp(cmd, "RBX_NORM")) {
    p2b_rbx_floats(&cur, in, 3);
    ((p2b_rbx_norm_fn)p2b_fn(0x350210A0ULL))(in, dst);
    p2b_rbx_print(dst, 3);
    p2b_rbx_print(in, 3);
  } else if (!strcmp(cmd, "RBX_MATMUL")) {
    p2b_rbx_floats(&cur, in, 18);
    ((p2b_rbx_matmul_fn)p2b_fn(0x35022380ULL))(dst, in, in + 9);
    p2b_rbx_print(dst, 9);
  } else {
    die("p2b: unknown rot_box command", cmd);
  }
  printf("\n");
  return 1;
}
