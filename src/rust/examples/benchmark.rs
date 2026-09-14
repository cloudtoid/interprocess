//! Repeatable in-process send + receive microbenchmark. Not cross-process latency.
//! Endpoints stay open throughout each workload; creation and cleanup are not timed.
use cloudtoid_interprocess::{Options, Publisher, Subscriber};
use std::{hint::black_box, time::Instant};
fn main() {
    const ITERATIONS: usize = 1_000_000;
    for size in [3, 50, 1024] {
        let options = Options::new(format!("b{}x{size}", std::process::id()), 1 << 20);
        let publisher = Publisher::open(&options).unwrap();
        let subscriber = Subscriber::open(&options).unwrap();
        let message = vec![42; size];
        let mut received = vec![0; size];
        let mut samples = Vec::new();
        for round in 0..12 {
            let start = Instant::now();
            for _ in 0..ITERATIONS {
                publisher.try_send(black_box(&message)).unwrap();
                assert_eq!(
                    subscriber
                        .try_recv_into(black_box(&mut received))
                        .unwrap(),
                    Some(size)
                );
                black_box(&received);
            }
            if round >= 4 {
                samples.push(start.elapsed().as_nanos() as f64 / ITERATIONS as f64);
            }
        }
        let mean = samples.iter().sum::<f64>() / samples.len() as f64;
        let deviation = (samples.iter().map(|x| (x - mean).powi(2)).sum::<f64>()
            / (samples.len() - 1) as f64)
            .sqrt();
        println!(
            "{size} bytes: {mean:.2} ns/roundtrip, stddev {deviation:.2}, samples {samples:?}"
        );
    }
}
