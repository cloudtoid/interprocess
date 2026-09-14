use super::*;
use std::{
    mem::{offset_of, size_of},
    sync::{atomic::AtomicUsize, Arc},
};

fn options(capacity: usize) -> Options {
    static NEXT: AtomicUsize = AtomicUsize::new(0);
    Options::new(
        format!("r{}x{}", std::process::id(), NEXT.fetch_add(1, Relaxed)),
        capacity,
    )
}

#[test]
fn protocol_layout() {
    assert_eq!(size_of::<Header>(), 32);
    assert_eq!(offset_of!(Header, read), 0);
    assert_eq!(offset_of!(Header, write), 8);
    assert_eq!(offset_of!(Header, reader), 16);
    assert_eq!(offset_of!(Header, notification), 24);
    assert_eq!(offset_of!(Header, participant), 28);
    assert_eq!(BUFFER_OFFSET, 262400);
}

#[test]
fn io_errors_preserve_their_source() {
    use std::error::Error as _;
    let error = Error::from(std::io::Error::from(std::io::ErrorKind::PermissionDenied));
    let source = error
        .source()
        .unwrap()
        .downcast_ref::<std::io::Error>()
        .unwrap();
    assert_eq!(source.kind(), std::io::ErrorKind::PermissionDenied);
    assert!(Error::CapacityMismatch.source().is_none());
}

#[test]
fn boundaries_and_wrap() {
    let options = options(64);
    let publisher = Publisher::open(&options).unwrap();
    let subscriber = Subscriber::open(&options).unwrap();
    assert_eq!(subscriber.try_recv().unwrap(), None);
    assert!(!publisher.try_send(&[0; 57]).unwrap());
    for length in (0..=56).cycle().take(2000) {
        let message: Vec<_> = (0..length).map(|n| n as u8).collect();
        publisher.try_send(&message).unwrap();
        assert_eq!(subscriber.try_recv().unwrap(), Some(message));
    }
    assert!(publisher.try_send(&[1; 56]).unwrap());
    assert!(matches!(publisher.try_send(&[]), Err(Error::Full)));
    let mut short = [0; 3];
    assert_eq!(subscriber.try_recv_into(&mut short).unwrap(), Some(3));
    assert_eq!(short, [1; 3]);
    assert_eq!(subscriber.try_recv().unwrap(), None);
}

#[test]
fn capacity_and_lifetime() {
    let options = options(64);
    let publisher = Publisher::open(&options).unwrap();
    let subscriber = Subscriber::open(&options).unwrap();
    assert!(matches!(
        Publisher::open(&Options {
            capacity: 128,
            ..options.clone()
        }),
        Err(Error::CapacityMismatch)
    ));
    publisher.try_send(b"survive").unwrap();
    drop(publisher);
    let other = Publisher::open(&options).unwrap();
    assert_eq!(subscriber.try_recv().unwrap().unwrap(), b"survive");
    drop(subscriber);
    other.try_send(b"still here").unwrap();
    let reader = Subscriber::open(&options).unwrap();
    assert_eq!(reader.try_recv().unwrap().unwrap(), b"still here");
    drop(reader);
    other.try_send(b"discard on last close").unwrap();
    drop(other);
    {
        let reopened = Subscriber::open(&options).unwrap();
        assert_eq!(reopened.try_recv().unwrap(), None);
    }
    let fresh = Subscriber::open(&Options {
        capacity: 128,
        ..options
    })
    .unwrap();
    assert_eq!(fresh.try_recv().unwrap(), None);
}

#[test]
fn batch_prefix_and_timeout() {
    let options = options(32);
    let publisher = Publisher::open(&options).unwrap();
    let subscriber = Subscriber::open(&options).unwrap();
    assert_eq!(publisher.try_send_batch(&[b"a", b"b", b"c"]).unwrap(), 2);
    assert_eq!(
        subscriber.recv_timeout(Duration::ZERO).unwrap().unwrap(),
        b"a"
    );
    assert_eq!(
        subscriber.recv_timeout(Duration::ZERO).unwrap().unwrap(),
        b"b"
    );
    assert!(subscriber
        .recv_timeout(Duration::from_millis(10))
        .unwrap()
        .is_none());
}

#[test]
fn concurrent_exactly_once() {
    let options = options(4096);
    let subscriber = Arc::new(Subscriber::open(&options).unwrap());
    let received = Arc::new((0..40_000).map(|_| AtomicUsize::new(0)).collect::<Vec<_>>());
    let total = AtomicUsize::new(0);
    std::thread::scope(|scope| {
        for producer in 0..4 {
            let publisher = Publisher::open(&options).unwrap();
            scope.spawn(move || {
                for i in 0..10_000u64 {
                    let value = (producer * 10_000 + i).to_le_bytes();
                    while matches!(publisher.try_send(&value), Err(Error::Full)) {
                        std::thread::yield_now();
                    }
                }
            });
        }
        for _ in 0..3 {
            let subscriber = &subscriber;
            let received = &received;
            let total = &total;
            scope.spawn(move || {
                let deadline = Instant::now() + Duration::from_secs(30);
                while total.load(Relaxed) < 40_000 {
                    assert!(Instant::now() < deadline, "delivery stalled");
                    if let Some(message) = subscriber.try_recv().unwrap() {
                        let value = u64::from_le_bytes(message.try_into().unwrap()) as usize;
                        assert_eq!(received[value].fetch_add(1, Relaxed), 0);
                        total.fetch_add(1, Relaxed);
                    } else {
                        std::thread::yield_now();
                    }
                }
            });
        }
    });
    assert!(received.iter().all(|n| n.load(Relaxed) == 1));
}

#[test]
fn publisher_limit_and_reuse() {
    let options = options(64);
    let mut publishers = (0..MAX_PUBLISHERS)
        .map(|_| Publisher::open(&options).unwrap())
        .collect::<Vec<_>>();
    assert!(matches!(
        Publisher::open(&options),
        Err(Error::PublisherLimit)
    ));
    publishers.pop();
    let replacement = Publisher::open(&options).unwrap();
    replacement.try_send(b"ok").unwrap();
}

#[test]
fn counters_do_not_wrap() {
    let options = options(64);
    let publisher = Publisher::open(&options).unwrap();
    let header = publisher.shared.header();
    header.participant.store(i32::MAX, SeqCst);
    assert!(matches!(Subscriber::open(&options), Err(Error::Exhausted)));
    header.read.store(i64::MAX - 7, SeqCst);
    header.write.store(i64::MAX - 7, SeqCst);
    assert!(matches!(publisher.try_send(&[]), Err(Error::Exhausted)));
}

#[test]
#[ignore = "subprocess fault-injection helper"]
fn crash_child() {
    let Ok(name) = std::env::var("CIP_CRASH_NAME") else {
        return;
    };
    let options = Options::new(name, 64);
    let mode = std::env::var("CIP_CRASH_MODE").unwrap();
    if mode == "publisher" {
        let publisher = Publisher::open(&options).unwrap();
        publisher.shared.active(publisher.slot).store(1, SeqCst);
        publisher.shared.header().write.store(16, SeqCst);
        println!("CRASH_READY");
        loop {
            std::thread::park();
        }
    } else {
        let subscriber = Subscriber::open(&options).unwrap();
        subscriber
            .shared
            .header()
            .reader
            .store(subscriber.id, SeqCst);
        subscriber.shared.gate().store(1, SeqCst);
        println!("CRASH_READY");
        loop {
            std::thread::park();
        }
    }
}

fn crashed_participant(options: &Options, mode: &str) -> std::process::Child {
    use std::io::{BufRead, BufReader};
    let mut child = std::process::Command::new(std::env::current_exe().unwrap())
        .args([
            "--exact",
            "queue::tests::crash_child",
            "--ignored",
            "--nocapture",
        ])
        .env("CIP_CRASH_NAME", &options.name)
        .env("CIP_CRASH_MODE", mode)
        .stdout(std::process::Stdio::piped())
        .spawn()
        .unwrap();
    let output = BufReader::new(child.stdout.take().unwrap());
    for line in output.lines() {
        if line.unwrap().contains("CRASH_READY") {
            return child;
        }
    }
    let _ = child.kill();
    let _ = child.wait();
    panic!("fault injection child did not start");
}

#[test]
fn killed_publisher_recovers_without_losing_queue() {
    let options = options(64);
    let subscriber = Subscriber::open(&options).unwrap();
    let mut child = crashed_participant(&options, "publisher");
    child.kill().unwrap();
    child.wait().unwrap();
    let publisher = Publisher::open(&options).unwrap();
    // Capture the abandoned tail before placing a later message behind it.
    assert!(subscriber.try_recv().unwrap().is_none());
    publisher.try_send(b"after crash").unwrap();
    let message = subscriber.recv_timeout(Duration::from_secs(20)).unwrap();
    assert_eq!(message.unwrap(), b"after crash");
}

#[test]
fn live_reader_is_retained_then_dead_empty_reader_reopens_gate() {
    let options = options(64);
    let subscriber = Subscriber::open(&options).unwrap();
    let publisher = Publisher::open(&options).unwrap();
    let mut child = crashed_participant(&options, "reader");
    let owner = subscriber.shared.header().reader.load(Acquire);
    subscriber.next_check.store(0, SeqCst);
    assert!(subscriber.try_recv().unwrap().is_none());
    assert_eq!(subscriber.shared.header().reader.load(Acquire), owner);
    assert!(matches!(publisher.try_send(b"blocked"), Err(Error::Full)));
    child.kill().unwrap();
    child.wait().unwrap();
    subscriber.next_check.store(0, SeqCst);
    assert!(subscriber.try_recv().unwrap().is_none());
    assert_eq!(subscriber.shared.header().reader.load(Acquire), 0);
    publisher.try_send(b"reopened").unwrap();
    assert_eq!(subscriber.try_recv().unwrap().unwrap(), b"reopened");
}

#[test]
fn a_paused_live_publisher_must_not_be_reclaimed() {
    let options = options(64);
    let subscriber = Subscriber::open(&options).unwrap();
    let mut child = crashed_participant(&options, "publisher");
    assert!(subscriber.try_recv().unwrap().is_none());
    let publisher = Publisher::open(&options).unwrap();
    publisher.try_send(b"preserved").unwrap();
    let stalled = subscriber.recv_timeout(Duration::from_secs(11)).unwrap();
    let read = subscriber.shared.header().read.load(Acquire);
    child.kill().unwrap();
    child.wait().unwrap();
    assert!(stalled.is_none());
    assert_eq!(read, 0, "a live writer's reservation was reclaimed");
    assert_eq!(
        subscriber
            .recv_timeout(Duration::from_secs(20))
            .unwrap()
            .unwrap(),
        b"preserved"
    );
}

#[test]
fn missed_notification_does_not_stall_a_blocking_receiver() {
    let options = options(64);
    let subscriber = Subscriber::open(&options).unwrap();
    let publisher = Publisher::open(&options).unwrap();
    subscriber.shared.header().notification.store(1, SeqCst);
    std::thread::scope(|scope| {
        scope.spawn(|| {
            std::thread::sleep(Duration::from_millis(15));
            publisher.try_send(b"no permit").unwrap();
        });
        assert_eq!(
            subscriber
                .recv_timeout(Duration::from_secs(1))
                .unwrap()
                .unwrap(),
            b"no permit"
        );
    });
}
