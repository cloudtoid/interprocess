use cloudtoid_interprocess::{Options, Publisher, Subscriber};
use std::time::Duration;
fn message(i: usize) -> Vec<u8> {
    let mut data = vec![0; 8 + i % 251];
    data[..8].copy_from_slice(&(i as u64).to_le_bytes());
    for (j, byte) in data.iter_mut().enumerate().skip(8) {
        *byte = ((i + j) % 251) as u8;
    }
    data
}
fn main() {
    let args = std::env::args().collect::<Vec<_>>();
    let options = Options::new(&args[2], 4096).with_path(&args[3]);
    let count = args[4].parse::<usize>().unwrap();
    if args[1] == "publish" {
        let publisher = Publisher::open(options).unwrap();
        for i in 0..count {
            let data = message(i);
            while !publisher.try_send(&data).unwrap() {
                std::thread::yield_now();
            }
        }
    } else {
        let subscriber = Subscriber::open(options).unwrap();
        println!("READY");
        for i in 0..count {
            assert_eq!(
                subscriber
                    .receive(Some(Duration::from_secs(30)))
                    .unwrap()
                    .unwrap(),
                message(i)
            );
        }
    }
}
