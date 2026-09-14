use core_queue::Options;
use napi::bindgen_prelude::*;
use napi_derive::napi;

fn error(e: core_queue::Error) -> Error<String> {
    let code = match e {
        core_queue::Error::Invalid(_) => "ERR_INVALID_ARGUMENT",
        core_queue::Error::CapacityMismatch => "ERR_CAPACITY_MISMATCH",
        core_queue::Error::PublisherLimit => "ERR_PUBLISHER_LIMIT",
        core_queue::Error::Exhausted => "ERR_EXHAUSTED",
        core_queue::Error::Corrupt => "ERR_CORRUPT",
        core_queue::Error::Io(_) => "ERR_IO",
    };
    Error::new(code.to_owned(), e.to_string())
}
fn closed() -> Error<String> {
    Error::new("ERR_CLOSED".into(), "endpoint is closed")
}
fn options(name: String, capacity: f64, path: Option<String>) -> Result<Options, String> {
    if !capacity.is_finite()
        || capacity.fract() != 0.0
        || !(0.0..=9_007_199_254_740_991.0).contains(&capacity)
    {
        return Err(Error::new(
            "ERR_INVALID_ARGUMENT".into(),
            "capacity must be a nonnegative safe integer",
        ));
    }
    let options = Options::new(name, capacity as usize);
    Ok(match path {
        Some(path) => options.with_path(path),
        None => options,
    })
}
#[napi]
pub struct Publisher {
    inner: Option<core_queue::Publisher>,
}
#[napi]
impl Publisher {
    #[napi(constructor)]
    pub fn new(name: String, capacity: f64, path: Option<String>) -> Result<Self, String> {
        Ok(Self {
            inner: Some(
                core_queue::Publisher::open(options(name, capacity, path)?).map_err(error)?,
            ),
        })
    }
    #[napi]
    pub fn try_send(&self, data: Uint8Array) -> Result<bool, String> {
        self.inner
            .as_ref()
            .ok_or_else(closed)?
            .try_send(&data)
            .map_err(error)
    }
    #[napi]
    pub fn try_send_batch(&self, messages: Vec<Uint8Array>) -> Result<u32, String> {
        let slices = messages.iter().map(|m| m.as_ref()).collect::<Vec<_>>();
        self.inner
            .as_ref()
            .ok_or_else(closed)?
            .try_send_batch(&slices)
            .map(|n| n as u32)
            .map_err(error)
    }
    #[napi]
    pub fn close(&mut self) {
        self.inner.take();
    }
}
#[napi]
pub struct Subscriber {
    inner: Option<core_queue::Subscriber>,
}
#[napi]
impl Subscriber {
    #[napi(constructor)]
    pub fn new(name: String, capacity: f64, path: Option<String>) -> Result<Self, String> {
        Ok(Self {
            inner: Some(
                core_queue::Subscriber::open(options(name, capacity, path)?).map_err(error)?,
            ),
        })
    }
    #[napi]
    pub fn try_receive(&self) -> Result<Option<Buffer>, String> {
        self.inner
            .as_ref()
            .ok_or_else(closed)?
            .try_receive()
            .map(|m| m.map(Buffer::from))
            .map_err(error)
    }
    #[napi]
    pub fn close(&mut self) {
        self.inner.take();
    }
}
