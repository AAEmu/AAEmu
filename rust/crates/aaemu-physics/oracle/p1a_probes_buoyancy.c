/* P1a probes for src/geometry/shapes/buoyancy.rs, #included by p1a_probes.c (after its helpers).
 * Add commands here and dispatch them from p1a_cmd_buoyancy (return 1 when handled). */
static void cmd_pressprobe(const Toks *t);

static int p1a_cmd_buoyancy(const char *c, const Toks *t) {
  if (!strcmp(c, "pressprobe")) {
    cmd_pressprobe(t);
    return 1;
  }
  return 0;
}

#include "p1a_probes_press.c"
