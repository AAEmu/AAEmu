/* P2b probe extension hook for the "poly" work group (owned by that group; see p2b_probe.c).
 * Handle internal-routine probe commands here and return 1; return 0 for lines it does not own.
 *
 * Polynomials are polynomial_tpl<double,N> as the engine stores them: `double denom; double
 * data[N+1]` (data[k] = coefficient of x^k). All doubles travel as 16-digit hex words.
 *
 *   PLYF <N> <lo> <hi> <maxiter> <deg> <known> <6 poly words> <4 root words>
 *        findroots: N = 4 -> sub_35149170, N = 3 -> sub_35050630; the root buffer (8 slots) is
 *        pre-filled with the 4 given words and 0x5A5A.. after them.
 *        answer: ret <hex32> <8 root words>
 *   PLYC <lo> <hi> <6 poly words>       sub_3514A2E0 (Sturm root count, degree 4)  -> ret <hex32>
 *   PLYD <n> <6 poly words>             sub_35148830 (derivative into a 6-word buffer of 0xA5..)
 *                                      -> 6 words
 *   PLYV <dn> <dd> <6 num> <6 den>      sub_351489E0 (division; quotient 8 words, remainder 6
 *                                      words, both pre-filled with 0xA5..) -> 8 + 6 words
 *   PLYM <name> <a> [<b>]               MSVCR100 double math: exp(a), log(a), sin(a), cos(a),
 *                                      atan2(a, b) -> 1 word
 */
typedef int (*p2b_poly_findroots)(void *poly, double lo, double hi, double *roots, int maxiter, int deg,
                                  char known);
typedef int (*p2b_poly_count)(void *poly, double lo, double hi);
typedef void (*p2b_poly_deriv)(void *src, void *dst, int n);
typedef void (*p2b_poly_div)(void *num, void *den, void *quot, void *rem, int dn, int dd);

static uint64_t p2b_poly_w64(char **cur) {
  char *t = p2b_tok(cur);
  if (!t) die("p2b poly: missing word", "");
  return _strtoui64(t, 0, 16);
}
static double p2b_poly_d(char **cur) {
  uint64_t u = p2b_poly_w64(cur);
  double d;
  memcpy(&d, &u, 8);
  return d;
}
static void p2b_poly_print_d(const double *v, int n) {
  for (int i = 0; i < n; i++) {
    uint64_t u;
    memcpy(&u, &v[i], 8);
    printf(" %016llx", (unsigned long long)u);
  }
}
static void p2b_poly_fill(double *v, int n) {
  uint64_t u = 0xA5A5A5A5A5A5A5A5ULL;
  for (int i = 0; i < n; i++) memcpy(&v[i], &u, 8);
}

static int p2b_ext_poly(char *line) {
  char *cur = line;
  while (*cur == ' ' || *cur == '\t') cur++;
  if (strncmp(cur, "PLY", 3) || !cur[3] || (cur[4] != ' ' && cur[4] != '\t')) return 0;
  char op = cur[3];
  cur += 5;
  __declspec(align(16)) double a[16], b[16], q[16], r[16];
  if (op == 'F') {
    int n = (int)p2b_poly_w64(&cur);
    double lo = p2b_poly_d(&cur), hi = p2b_poly_d(&cur);
    int maxiter = (int)p2b_poly_w64(&cur), deg = (int)p2b_poly_w64(&cur), known = (int)p2b_poly_w64(&cur);
    p2b_poly_fill(a, 16);
    for (int i = 0; i < 6; i++) a[i] = p2b_poly_d(&cur);
    uint64_t s = 0x5A5A5A5A5A5A5A5AULL;
    for (int i = 0; i < 16; i++) memcpy(&r[i], &s, 8);
    for (int i = 0; i < 4; i++) r[i] = p2b_poly_d(&cur);
    if (deg < 0 || deg > n) die("p2b poly: bad degree", "");
    p2b_poly_findroots f = (p2b_poly_findroots)p2b_fn(n == 3 ? 0x35050630ULL : 0x35149170ULL);
    int ret = f(a, lo, hi, r, maxiter, deg, (char)known);
    printf("ret %08x", (uint32_t)ret);
    p2b_poly_print_d(r, 8);
    printf("\n");
    return 1;
  }
  if (op == 'C') {
    double lo = p2b_poly_d(&cur), hi = p2b_poly_d(&cur);
    for (int i = 0; i < 6; i++) a[i] = p2b_poly_d(&cur);
    int ret = ((p2b_poly_count)p2b_fn(0x3514A2E0ULL))(a, lo, hi);
    printf("ret %08x\n", (uint32_t)ret);
    return 1;
  }
  if (op == 'D') {
    int n = (int)p2b_poly_w64(&cur);
    if (n < 0 || n > 4) die("p2b poly: bad n", "");
    for (int i = 0; i < 6; i++) a[i] = p2b_poly_d(&cur);
    p2b_poly_fill(b, 16);
    ((p2b_poly_deriv)p2b_fn(0x35148830ULL))(a, b, n);
    printf("D");
    p2b_poly_print_d(b, 6);
    printf("\n");
    return 1;
  }
  if (op == 'V') {
    int dn = (int)p2b_poly_w64(&cur), dd = (int)p2b_poly_w64(&cur);
    if (dn < -1 || dn > 4 || dd < -1 || dd > 4) die("p2b poly: bad degrees", "");
    for (int i = 0; i < 6; i++) a[i] = p2b_poly_d(&cur);
    for (int i = 0; i < 6; i++) b[i] = p2b_poly_d(&cur);
    p2b_poly_fill(q, 16);
    p2b_poly_fill(r, 16);
    ((p2b_poly_div)p2b_fn(0x351489E0ULL))(a, b, q, r, dn, dd);
    printf("V");
    p2b_poly_print_d(q, 8);
    p2b_poly_print_d(r, 6);
    printf("\n");
    return 1;
  }
  if (op == 'M') {
    char *name = p2b_tok(&cur);
    if (!name) die("p2b poly: missing name", "");
    double x = p2b_poly_d(&cur), y = 0.0, res;
    HMODULE crt = GetModuleHandleA("MSVCR100.dll");
    void *fp = crt ? (void *)GetProcAddress(crt, name) : 0;
    if (!fp) die("p2b poly: no CRT function", name);
    if (!strcmp(name, "atan2")) {
      y = p2b_poly_d(&cur);
      res = ((double (*)(double, double))fp)(x, y);
    } else {
      res = ((double (*)(double))fp)(x);
    }
    printf("M");
    p2b_poly_print_d(&res, 1);
    printf("\n");
    return 1;
  }
  return 0;
}
