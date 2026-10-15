"""Tests for the S->C coverage ratchet logic (no Rust sources needed)."""

import unittest

from sc_usage import baseline_from_report, check_against_baseline


def report(typeless, defined_only, unclear=()):
    return {
        "summary": {"sc_opcodes": 10},
        "opcodes_without_any_type": [{"name": n, "opcode": i} for i, n in enumerate(typeless)],
        "defined_only": [{"name": n} for n in defined_only],
        "unclear": [{"name": n} for n in unclear],
    }


class RatchetTests(unittest.TestCase):
    def setUp(self):
        self.base = baseline_from_report(report(["SCB", "SCA"], ["SCD", "SCC"]))

    def test_baseline_is_sorted_with_counts(self):
        self.assertEqual(self.base["typeless"], ["SCA", "SCB"])
        self.assertEqual(self.base["never_sent"], ["SCC", "SCD"])
        self.assertEqual(
            self.base["counts"], {"sc_opcodes": 10, "typeless": 2, "never_sent": 2}
        )

    def test_unclear_counts_as_never_sent(self):
        b = baseline_from_report(report([], ["SCD"], ["SCE"]))
        self.assertEqual(b["never_sent"], ["SCD", "SCE"])

    def test_unchanged_passes_silently(self):
        self.assertEqual(check_against_baseline(self.base, self.base), ([], []))

    def test_new_typeless_opcode_fails(self):
        cur = baseline_from_report(report(["SCA", "SCB", "SCZ"], ["SCC", "SCD"]))
        errors, _ = check_against_baseline(self.base, cur)
        self.assertEqual(len(errors), 1)
        self.assertIn("typeless", errors[0])
        self.assertIn("SCZ", errors[0])

    def test_new_never_sent_type_fails(self):
        cur = baseline_from_report(report(["SCA", "SCB"], ["SCC", "SCD", "SCY"]))
        errors, _ = check_against_baseline(self.base, cur)
        self.assertEqual(len(errors), 1)
        self.assertIn("never_sent", errors[0])
        self.assertIn("SCY", errors[0])

    def test_shrinking_passes_with_hint(self):
        cur = baseline_from_report(report(["SCA"], ["SCC", "SCD"]))
        errors, hints = check_against_baseline(self.base, cur)
        self.assertEqual(errors, [])
        self.assertEqual(len(hints), 1)
        self.assertIn("--update-baseline", hints[0])

    def test_swap_is_a_regression(self):
        # One gap closed and a different one opened is still a new member.
        cur = baseline_from_report(report(["SCA", "SCQ"], ["SCC", "SCD"]))
        errors, hints = check_against_baseline(self.base, cur)
        self.assertEqual(len(errors), 1)
        self.assertEqual(len(hints), 1)


if __name__ == "__main__":
    unittest.main()
