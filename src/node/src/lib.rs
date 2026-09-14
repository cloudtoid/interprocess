use core_queue::Options;
use napi::{bindgen_prelude::*, Env, Task};
use napi_derive::napi;
use std::{sync::Arc, time::Duration};

fn error(e: impl std::fmt::Display) -> Error {
    Error::from_reason(e.to_string())
}
fn options(name: String, capacity: u32, path: Option<String>) -> Options {
    let options = Options::new(name, capacity as usize);
    match path {
        Some(path) => options.with_path(path),
        None => options,
    }
}
#[napi]
pub struct Publisher {
    inner: Option<core_queue::Publisher>,
}
#[napi]
impl Publisher {
    #[napi(constructor)]
    pub fn new(name: String, capacity: u32, path: Option<String>) -> Result<Self> {
        Ok(Self {
            inner: Some(core_queue::Publisher::open(options(name, capacity, path)).map_err(error)?),
        })
    }
    #[napi]
    pub fn try_send(&self, data: Buffer) -> Result<bool> {
        self.inner
            .as_ref()
            .ok_or_else(|| error("publisher is closed"))?
            .try_send(&data)
            .map_err(error)
    }
    #[napi]
    pub fn try_send_batch(&self, messages: Vec<Buffer>) -> Result<u32> {
        let slices = messages.iter().map(|m| m.as_ref()).collect::<Vec<_>>();
        self.inner
            .as_ref()
            .ok_or_else(|| error("publisher is closed"))?
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
    inner: Option<Arc<core_queue::Subscriber>>,
}
#[napi]
impl Subscriber {
    #[napi(constructor)]
    pub fn new(name: String, capacity: u32, path: Option<String>) -> Result<Self> {
        Ok(Self {
            inner: Some(Arc::new(
                core_queue::Subscriber::open(options(name, capacity, path)).map_err(error)?,
            )),
        })
    }
    #[napi]
    pub fn try_receive(&self) -> Result<Option<Buffer>> {
        self.inner
            .as_ref()
            .ok_or_else(|| error("subscriber is closed"))?
            .try_receive()
            .map(|m| m.map(Buffer::from))
            .map_err(error)
    }
    #[napi]
    pub fn receive(&self, timeout_ms: u32) -> Result<AsyncTask<Receive>> {
        Ok(AsyncTask::new(Receive {
            subscriber: self
                .inner
                .as_ref()
                .ok_or_else(|| error("subscriber is closed"))?
                .clone(),
            timeout: Duration::from_millis(timeout_ms as u64),
        }))
    }
    #[napi]
    pub fn close(&mut self) {
        self.inner.take();
    }
}
pub struct Receive {
    subscriber: Arc<core_queue::Subscriber>,
    timeout: Duration,
}
impl Task for Receive {
    type Output = Option<Vec<u8>>;
    type JsValue = Option<Buffer>;
    fn compute(&mut self) -> Result<Self::Output> {
        self.subscriber.receive(Some(self.timeout)).map_err(error)
    }
    fn resolve(&mut self, _env: Env, output: Self::Output) -> Result<Self::JsValue> {
        Ok(output.map(Buffer::from))
    }
}
