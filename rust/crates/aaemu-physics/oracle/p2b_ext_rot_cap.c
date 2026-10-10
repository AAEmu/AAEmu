/* P2b probe extension hook for the "rot_cap" work group (owned by that group; see p2b_probe.c).
 * Handle internal-routine probe commands here and return 1; return 0 for lines it does not own.
 *
 *   RCAPLOG U <case words...>
 *
 * Runs an unprojection case like `U`, with the two polynomial routines the rotational ray-capsule
 * cell calls (sub_3514A2E0 "has a root in [lo, hi]" and sub_35149170 "find roots") hooked: their
 * entry points are patched with an absolute jump to a logger that restores the original bytes,
 * calls the original routine, re-patches and prints the call. The answer line is the log followed
 * by the normal `ret ...` answer:
 *
 *   H <6 coefficient qwords> <lo> <hi> <ret>                 (one per sub_3514A2E0 call)
 *   S <6 coefficient qwords> <lo> <hi> <max> <it> <fl> <ret> <ret roots as qwords>   (sub_35149170)
 *
 * All doubles are printed as 16 hex digits (raw bits). Used by the rot_cap tests to verify the
 * polynomial set-up separately from the solvers and to replay the DLL's roots into the port.
 */

typedef int (*rcap_has_fn)(double *p, double lo, double hi);
typedef int (*rcap_roots_fn)(double *p, double lo, double hi, double *roots, int maxr, int it, char fl);

static void p2b_unproj(char *cur);
static unsigned char rcap_saved_h[14], rcap_saved_s[14];

static void rcap_patch(void *at, void *to, unsigned char *save) {
  DWORD old;
  VirtualProtect(at, 14, PAGE_EXECUTE_READWRITE, &old);
  if (save) memcpy(save, at, 14);
  unsigned char j[14] = {0xFF, 0x25, 0, 0, 0, 0};
  memcpy(j + 6, &to, 8);
  memcpy(at, j, 14);
  VirtualProtect(at, 14, old, &old);
  FlushInstructionCache(GetCurrentProcess(), at, 14);
}
static void rcap_restore(void *at, const unsigned char *save) {
  DWORD old;
  VirtualProtect(at, 14, PAGE_EXECUTE_READWRITE, &old);
  memcpy(at, save, 14);
  VirtualProtect(at, 14, old, &old);
  FlushInstructionCache(GetCurrentProcess(), at, 14);
}
static void rcap_pd(double d) {
  uint64_t u;
  memcpy(&u, &d, 8);
  printf(" %016llx", (unsigned long long)u);
}

static int rcap_has_hook(double *p, double lo, double hi);
static int rcap_roots_hook(double *p, double lo, double hi, double *roots, int maxr, int it, char fl);

static int rcap_has_hook(double *p, double lo, double hi) {
  void *at = p2b_fn(0x3514A2E0ULL);
  double c[6];
  memcpy(c, p, sizeof c);
  rcap_restore(at, rcap_saved_h);
  int r = ((rcap_has_fn)at)(p, lo, hi);
  rcap_patch(at, (void *)rcap_has_hook, 0);
  printf("H");
  for (int i = 0; i < 6; i++) rcap_pd(c[i]);
  rcap_pd(lo);
  rcap_pd(hi);
  printf(" %x ", (unsigned)r);
  return r;
}

static int rcap_roots_hook(double *p, double lo, double hi, double *roots, int maxr, int it, char fl) {
  void *at = p2b_fn(0x35149170ULL);
  double c[6];
  memcpy(c, p, sizeof c);
  rcap_restore(at, rcap_saved_s);
  int r = ((rcap_roots_fn)at)(p, lo, hi, roots, maxr, it, fl);
  rcap_patch(at, (void *)rcap_roots_hook, 0);
  printf("S");
  for (int i = 0; i < 6; i++) rcap_pd(c[i]);
  rcap_pd(lo);
  rcap_pd(hi);
  printf(" %x %x %x %x", (unsigned)maxr, (unsigned)it, (unsigned)(unsigned char)fl, (unsigned)r);
  for (int i = 0; i < r && i < maxr; i++) rcap_pd(roots[i]);
  printf(" ");
  return r;
}

static int p2b_ext_rot_cap(char *line) {
  char *s = line;
  while (*s == ' ' || *s == '\t') s++;
  if (strncmp(s, "RCAPLOG ", 8)) return 0;
  s += 8;
  while (*s == ' ' || *s == '\t') s++;
  if (s[0] != 'U') die("p2b: RCAPLOG expects a U case", line);
  void *h = p2b_fn(0x3514A2E0ULL), *r = p2b_fn(0x35149170ULL);
  rcap_patch(h, (void *)rcap_has_hook, rcap_saved_h);
  rcap_patch(r, (void *)rcap_roots_hook, rcap_saved_s);
  printf("L ");
  p2b_unproj(s + 2);
  rcap_restore(h, rcap_saved_h);
  rcap_restore(r, rcap_saved_s);
  return 1;
}
