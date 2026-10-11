/* P1a probes for CTriMesh slot 12 CalcVolumetricPressure (0x351CBE20) through the box's slot 12
 * (0x3513B0F0, temporary 12-triangle mesh). #included by p1a_probes_buoyancy.c.
 *
 *   pressprobe mode=M n=COUNT seed=SEED
 *
 * prints the same lines as `gprobe probe=press type=box`:
 *   gp press 0 P <box 16 words> I <gwd 13> <epicenter 3> <k> <rmin> <com 3> <P in 3> <L in 3> O <P 3> <L 3>
 *
 * modes (epicenter / parameter distributions):
 *   0 near the box            1 far away                 2 inside the box
 *   3 rmin larger than the distances                     4 degenerate boxes (zero / tiny sizes)
 *   5 extreme scales          6 axis-aligned integer grid (exact ties)
 *   7 exactly on a face / edge / corner of the box       8 very close to a face, rmin ~ 0
 *     (deep subdivision: depth limit and the 300-pop cap)
 *   9 negative box sizes      10 mirrored box basis (det -1)
 *     (9/10 flip the winding against the face normals: the only way a box reaches the
 *     projection split, which needs the epicenter "outside" all three edges)
 */

/* world position of the box-local point l (vertex = center + Basis^T l, then gwd) */
static void p1a_press_box_world(const float *prim, const float *gwd, const float *l, float *w) {
  float b[3];
  for (int i = 0; i < 3; i++)
    b[i] = prim[10 + i] + (prim[i] * l[0] + prim[3 + i] * l[1] + prim[6 + i] * l[2]);
  for (int i = 0; i < 3; i++)
    w[i] = gwd[i] + gwd[12] * (gwd[3 + 3 * i] * b[0] + gwd[4 + 3 * i] * b[1] + gwd[5 + 3 * i] * b[2]);
}

static void cmd_pressprobe(const Toks *t) {
  int n = atoi(get(t, "n", "100"));
  int mode = atoi(get(t, "mode", "0"));
  g_rng = strtoull(get(t, "seed", "1"), 0, 0);
  for (int i = 0; i < n; i++) {
    __declspec(align(16)) float prim[32], gwd[32], in[64], out[64], l[3];
    float c[3];
    float rad = p1a_rand_prim(0, prim);
    p1a_rand_gwd(gwd, 0);
    if (mode == 4) {
      int a = (int)frand(0, 2.999f);
      float r = frand(0, 1);
      float v = r < 0.4f ? 0.0f : (r < 0.7f ? 1e-7f : 1e-3f);
      prim[13 + a] = v;
      if (frand(0, 1) < 0.3f) prim[13 + (a + 1) % 3] = v;
      if (frand(0, 1) < 0.1f) prim[13] = prim[14] = prim[15] = v;
    } else if (mode == 9) {
      int a = (int)frand(0, 2.999f);
      prim[13 + a] = -prim[13 + a];
      if (frand(0, 1) < 0.3f) {
        prim[13 + (a + 1) % 3] = -prim[13 + (a + 1) % 3];
        prim[13 + (a + 2) % 3] = -prim[13 + (a + 2) % 3];
      }
    } else if (mode == 10) {
      int a = (int)frand(0, 2.999f);
      for (int k = 0; k < 3; k++) prim[3 * a + k] = -prim[3 * a + k];
    } else if (mode == 5) {
      gwd[12] = frand(0, 1) < 0.5f ? frand(0.01f, 0.2f) : frand(5, 100);
    } else if (mode == 6) {
      p1a_identity(prim);
      p1a_identity(gwd + 3);
      gwd[12] = 1;
      for (int k = 0; k < 3; k++) {
        prim[10 + k] = (float)(int)frand(-2, 2.999f) * 0.5f;
        prim[13 + k] = (float)(int)frand(1, 4.999f) * 0.5f;
        gwd[k] = (float)(int)frand(-2, 2.999f);
      }
    }
    void *g = p1a_create(0, prim);
    p1a_world_center(0, prim, gwd, c);
    float s = gwd[12], R = rad * s + 0.2f;
    in[3] = frand(0.1f, 100);  /* k */
    in[4] = frand(0.001f, 3); /* rmin */
    switch (mode) {
    default:
    case 0: case 4: case 5: case 9: case 10:
      for (int k = 0; k < 3; k++) in[k] = c[k] + frand(-1.5f, 1.5f) * R;
      break;
    case 1: {
      float d[3];
      p1a_rand_unit(d);
      float f = frand(2, 40) * R;
      for (int k = 0; k < 3; k++) in[k] = c[k] + d[k] * f;
      break;
    }
    case 2:
      for (int k = 0; k < 3; k++) l[k] = frand(-1, 1) * prim[13 + k];
      p1a_press_box_world(prim, gwd, l, in);
      break;
    case 3:
      for (int k = 0; k < 3; k++) in[k] = c[k] + frand(-2, 2) * R;
      in[4] = frand(3, 200);
      break;
    case 6:
      for (int k = 0; k < 3; k++) in[k] = c[k] + (float)(int)frand(-8, 8.999f) * 0.5f;
      in[3] = (float)(int)frand(1, 10.999f);
      in[4] = frand(0, 1) < 0.5f ? (float)(int)frand(0, 4.999f) : (float)(int)frand(0, 8.999f) * 0.25f;
      break;
    case 7: {
      int a = (int)frand(0, 2.999f);
      for (int k = 0; k < 3; k++) l[k] = frand(-1, 1) * prim[13 + k];
      l[a] = frand(0, 1) < 0.5f ? -prim[13 + a] : prim[13 + a];
      if (frand(0, 1) < 0.3f) l[(a + 1) % 3] = prim[13 + (a + 1) % 3];
      if (frand(0, 1) < 0.15f) l[(a + 2) % 3] = -prim[13 + (a + 2) % 3];
      p1a_press_box_world(prim, gwd, l, in);
      break;
    }
    case 8: {
      int a = (int)frand(0, 2.999f);
      for (int k = 0; k < 3; k++) l[k] = frand(-1.2f, 1.2f) * prim[13 + k];
      l[a] = (frand(0, 1) < 0.5f ? -1.0f : 1.0f) * (prim[13 + a] + frand(1e-4f, 0.05f));
      p1a_press_box_world(prim, gwd, l, in);
      in[4] = frand(0, 1) < 0.5f ? 0.0f : frand(0, 1e-3f);
      break;
    }
    }
    for (int k = 0; k < 3; k++) in[5 + k] = gwd[19 + k]; /* com */
    for (int k = 0; k < 6; k++) out[k] = (i % 3 == 0) ? 0.0f : frand(-2, 2);
    printf("gp press 0");
    p1a_out_w("P", prim, 16);
    p1a_out_w("I", gwd, 13);
    p1a_out_w(0, in, 8);
    p1a_out_w(0, out, 6);
    ((p1a_fn_press)VT(g)[P1A_VolPressure])(g, gwd, in, in[3], in[4], in + 5, out, out + 3);
    p1a_out_w("O", out, 6);
    printf("\n");
    p1a_release(g);
  }
}
