//! Concurrent bots: N accounts running a scenario at the same time, with per-step latencies.

use std::collections::BTreeMap;
use std::time::{Duration, Instant};

use crate::record::Recorder;
use crate::scenario::{self, BotConfig, Kind, Outcome};

#[derive(Debug, Default)]
pub struct LoadReport {
    pub bots: usize,
    pub ok: usize,
    pub errors: Vec<String>,
    pub wall: Duration,
    /// step -> latencies (ms) of the bots that completed it.
    pub latencies: BTreeMap<String, Vec<u64>>,
}

fn pct(v: &[u64], p: f64) -> u64 {
    if v.is_empty() {
        return 0;
    }
    let i = ((v.len() as f64 - 1.0) * p).round() as usize;
    v[i.min(v.len() - 1)]
}

impl LoadReport {
    pub fn render(&self) -> String {
        let mut s = format!(
            "{} bots, {} ok, {} failed, wall {:.1}s\n",
            self.bots,
            self.ok,
            self.bots - self.ok,
            self.wall.as_secs_f64()
        );
        s.push_str(&format!(
            "{:<20} {:>5} {:>8} {:>8} {:>8} {:>8}\n",
            "step", "n", "p50 ms", "p95 ms", "max ms", "mean ms"
        ));
        for (step, v) in &self.latencies {
            let mut v = v.clone();
            v.sort_unstable();
            let mean = v.iter().sum::<u64>() as f64 / v.len().max(1) as f64;
            s.push_str(&format!(
                "{:<20} {:>5} {:>8} {:>8} {:>8} {:>8.0}\n",
                step,
                v.len(),
                pct(&v, 0.5),
                pct(&v, 0.95),
                v.last().copied().unwrap_or(0),
                mean
            ));
        }
        for e in self.errors.iter().take(20) {
            s.push_str(&format!("error: {e}\n"));
        }
        s
    }
}

/// Runs `n` bots (`{prefix}{i}` accounts and characters) concurrently, starting one every
/// `ramp`.
pub async fn run(
    kind: Kind,
    base: &BotConfig,
    prefix: &str,
    n: usize,
    ramp: Duration,
) -> LoadReport {
    let start = Instant::now();
    let mut tasks = Vec::new();
    for i in 0..n {
        let mut cfg = base.clone();
        cfg.user = format!("{prefix}{i}");
        cfg.character = format!("{prefix}{i}");
        tasks.push(tokio::spawn(async move {
            let rec = Recorder::new(false);
            let o: Outcome = scenario::run(kind, &cfg, &rec).await;
            (cfg.user, o)
        }));
        tokio::time::sleep(ramp).await;
    }
    let mut report = LoadReport {
        bots: n,
        ..Default::default()
    };
    for t in tasks {
        match t.await {
            Ok((user, o)) => {
                if o.ok() {
                    report.ok += 1;
                } else if let Some(e) = &o.error {
                    report.errors.push(format!("{user}: {e}"));
                }
                for s in o.steps.iter().filter(|s| s.ok) {
                    report
                        .latencies
                        .entry(s.name.clone())
                        .or_default()
                        .push(s.ms);
                }
            }
            Err(e) => report.errors.push(format!("bot task panicked: {e}")),
        }
    }
    report.wall = start.elapsed();
    report
}
