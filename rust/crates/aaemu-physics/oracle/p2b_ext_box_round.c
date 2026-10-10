/* P2b probe extension hook for the "box_round" work group (owned by that group; see p2b_probe.c).
 * Handle internal-routine probe commands here and return 1; return 0 for lines it does not own. */
static int p2b_ext_box_round(char *line) {
  (void)line;
  return 0;
}
