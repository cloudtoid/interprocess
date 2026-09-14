use cloudtoid_interprocess::{Error, Options, Publisher, Subscriber};
use std::{
    io::{self, Write},
    time::Duration,
};
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
    if args[1].starts_with("hold-") {
        let _publisher;
        let _subscriber;
        if args[1] == "hold-publisher" {
            _publisher = Publisher::open(&options).unwrap();
        } else {
            _subscriber = Subscriber::open(&options).unwrap();
        }
        println!("READY");
        io::stdout().flush().unwrap();
        io::stdin().read_line(&mut String::new()).unwrap();
    } else if args[1] == "publish" {
        let publisher = Publisher::open(&options).unwrap();
        let start = args.get(5).map_or(0, |s| s.parse::<usize>().unwrap());
        if args.len() > 5 {
            println!("READY");
            io::stdout().flush().unwrap();
            io::stdin().read_line(&mut String::new()).unwrap();
        }
        for i in start..start + count {
            let data = message(i);
            while matches!(publisher.try_send(&data), Err(Error::Full)) {
                std::thread::yield_now();
            }
        }
    } else {
        let subscriber = Subscriber::open(&options).unwrap();
        println!("READY");
        if args[1] == "collect" {
            loop {
                let data = subscriber
                    .recv_timeout(Duration::from_secs(30))
                    .unwrap()
                    .unwrap();
                if data.is_empty() {
                    break;
                }
                let id = u64::from_le_bytes(data[..8].try_into().unwrap()) as usize;
                assert_eq!(data, message(id));
                println!("{id}");
            }
            return;
        }
        for i in 0..count {
            assert_eq!(
                subscriber
                    .recv_timeout(Duration::from_secs(30))
                    .unwrap()
                    .unwrap(),
                message(i)
            );
        }
    }
}
