/* P2b probe extension hook for the "box_box" work group (owned by that group; see p2b_probe.c).
 * Handle internal-routine probe commands here and return 1; return 0 for lines it does not own.
 *
 *   BBCLIP <n> <code> <edge> <half0> <half1> <2n pt words> <n id words>
 *     sub_3516E400(src, ids, n, half, code, edge, dst, dst_ids) with 64-entry destination buffers
 *     pre-filled with 0xDEAD0000+i / 0xBEEF0000+i. Answer: `clip <ret>` and the first
 *     min(ret+1, 64) destination entries (x, y, id).
 *   BBSEG <h> <i> <id0> <id1> <4 seg words> <2 ids0 words> <2 ids1 words>
 *     sub_3516E6C0(seg, h, i, ids0, ids1, id0, id1). Answer: `seg <ret>`, seg[4], ids0[2], ids1[2].
 * All values are hex words (floats as raw bits). */
typedef unsigned int (*p2b_bb_clip_fn)(float *src, int *ids, unsigned int n, float *half, int code, int edge,
                                      float *dst, int *dst_ids);
typedef int (*p2b_bb_seg_fn)(float *seg, float h, int i, int *ids0, int *ids1, int id0, int id1);

static int p2b_ext_box_box(char *line) {
  char *cur = line;
  while (*cur == ' ' || *cur == '\t') cur++;
  /* match the keyword without modifying the line (later hooks see it unchanged) */
  if (!strncmp(cur, "BBCLIP ", 7)) {
    cur += 7;
    __declspec(align(16)) float src[128], dst[128], half[2];
    int ids[64], dids[64];
    int n = (int)p2b_word(&cur);
    int code = (int)p2b_word(&cur);
    int edge = (int)p2b_word(&cur);
    if (n < 0 || n > 64) die("p2b: BBCLIP n", "");
    half[0] = bitsf(p2b_word(&cur));
    half[1] = bitsf(p2b_word(&cur));
    for (int i = 0; i < 2 * n; i++) src[i] = bitsf(p2b_word(&cur));
    for (int i = 0; i < n; i++) ids[i] = (int)p2b_word(&cur);
    for (int i = 0; i < 64; i++) {
      dst[2 * i] = bitsf(0xDEAD0000u + i);
      dst[2 * i + 1] = bitsf(0xDEAD4000u + i);
      dids[i] = (int)(0xBEEF0000u + i);
    }
    p2b_bb_clip_fn f = (p2b_bb_clip_fn)p2b_fn(0x3516E400ULL);
    unsigned int r = f(src, ids, (unsigned int)n, half, code, edge, dst, dids);
    printf("clip %08x", r);
    int m = (int)r + 1;
    if (m > 64) m = 64;
    for (int i = 0; i < m; i++) printf(" %08x %08x %08x", fbits(dst[2 * i]), fbits(dst[2 * i + 1]), (uint32_t)dids[i]);
    printf("\n");
    return 1;
  }
  if (!strncmp(cur, "BBSEG ", 6)) {
    cur += 6;
    __declspec(align(16)) float seg[4];
    int ids0[2], ids1[2];
    float h = bitsf(p2b_word(&cur));
    int i = (int)p2b_word(&cur);
    int id0 = (int)p2b_word(&cur);
    int id1 = (int)p2b_word(&cur);
    if (i < 0 || i > 1) die("p2b: BBSEG i", "");
    for (int k = 0; k < 4; k++) seg[k] = bitsf(p2b_word(&cur));
    for (int k = 0; k < 2; k++) ids0[k] = (int)p2b_word(&cur);
    for (int k = 0; k < 2; k++) ids1[k] = (int)p2b_word(&cur);
    p2b_bb_seg_fn f = (p2b_bb_seg_fn)p2b_fn(0x3516E6C0ULL);
    int r = f(seg, h, i, ids0, ids1, id0, id1);
    printf("seg %08x", (uint32_t)r);
    for (int k = 0; k < 4; k++) printf(" %08x", fbits(seg[k]));
    printf(" %08x %08x %08x %08x\n", (uint32_t)ids0[0], (uint32_t)ids0[1], (uint32_t)ids1[0], (uint32_t)ids1[1]);
    return 1;
  }
  return 0;
}
