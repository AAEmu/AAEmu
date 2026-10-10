/* P2b probe extension hook for the "cyl_cyl" work group (owned by that group; see p2b_probe.c).
 * Handle internal-routine probe commands here and return 1; return 0 for lines it does not own.
 *
 *   CCSIN    -> "ccsin <1025 words>": the quarter-wave sine table at 0x3537B450 (built by
 *               sub_351E5900, sinf(i * pi/2048) for i = 0..1024) as the cyl-cyl cell sees it. */
static int p2b_ext_cyl_cyl(char *line) {
  char *s = line;
  while (*s == ' ' || *s == '\t') s++;
  if (!strncmp(s, "CCSIN", 5)) {
    printf("ccsin");
    for (int i = 0; i <= 1024; i++) printf(" %08x", *(uint32_t *)p2b_fn(0x3537B450ULL + 4ULL * i));
    printf("\n");
    return 1;
  }
  return 0;
}
