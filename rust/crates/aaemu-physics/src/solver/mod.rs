//! **P4b — contact solver** (stub): `InvokeContactSolver` 0x3509E850 (x86 0x350A62B0;
//! "PreCG", "LCPCG", "LCPCG-unproj", "LCPMC"): CG fast path, projected Gauss–Seidel with a
//! cumulative friction cone (stage 1), LCP-CG (stage 2), positional unprojection (stage 3).
//! Sort bodies by a stable id, not by address. Stage-1 iteration budget: see
//! CRYPHYSICS-MAP Appendix A (resolved: `max(4·nC, 6000)`). `dynamics.md` §3.
