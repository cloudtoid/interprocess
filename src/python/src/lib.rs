use core_queue::Options;
use pyo3::{
    exceptions::{PyOverflowError, PyRuntimeError, PyValueError},
    prelude::*,
    types::{PyBytes, PyMemoryView},
};
use std::{
    path::PathBuf,
    sync::RwLock,
    time::{Duration, Instant},
};

pyo3::create_exception!(cloudtoid_interprocess, CapacityMismatchError, PyValueError);
pyo3::create_exception!(cloudtoid_interprocess, PublisherLimitError, PyRuntimeError);
pyo3::create_exception!(cloudtoid_interprocess, CorruptQueueError, PyRuntimeError);

fn error(e: core_queue::Error) -> PyErr {
    match e {
        core_queue::Error::Invalid(message) => PyValueError::new_err(message),
        core_queue::Error::CapacityMismatch => CapacityMismatchError::new_err(e.to_string()),
        core_queue::Error::PublisherLimit => PublisherLimitError::new_err(e.to_string()),
        core_queue::Error::Exhausted => PyOverflowError::new_err(e.to_string()),
        core_queue::Error::Corrupt => CorruptQueueError::new_err(e.to_string()),
        core_queue::Error::Io(error) => error.into(),
        _ => PyRuntimeError::new_err(e.to_string()),
    }
}
fn closed() -> PyErr {
    PyValueError::new_err("endpoint is closed")
}

// Keep bytes on the direct path. Other buffer providers are snapshotted while
// holding the GIL, including noncontiguous memoryviews; no borrowed buffer escapes.
fn bytes<'py>(data: &Bound<'py, PyAny>) -> PyResult<Bound<'py, PyBytes>> {
    if let Ok(bytes) = data.cast::<PyBytes>() {
        return Ok(bytes.clone());
    }
    Ok(PyMemoryView::from(data)?
        .call_method0("tobytes")?
        .cast_into::<PyBytes>()?)
}
fn options(name: String, capacity: usize, path: Option<PathBuf>) -> Options {
    let options = Options::new(name, capacity);
    match path {
        Some(path) => options.with_path(path),
        None => options,
    }
}

#[pyclass(module = "cloudtoid_interprocess._native")]
struct Publisher {
    inner: Option<core_queue::Publisher>,
}
#[pymethods]
impl Publisher {
    #[new]
    #[pyo3(signature = (name, capacity, path=None))]
    fn new(name: String, capacity: usize, path: Option<PathBuf>) -> PyResult<Self> {
        Ok(Self {
            inner: Some(
                core_queue::Publisher::open(&options(name, capacity, path)).map_err(error)?,
            ),
        })
    }
    fn try_send(&self, data: &Bound<'_, PyAny>) -> PyResult<bool> {
        self.inner
            .as_ref()
            .ok_or_else(closed)?
            .try_send(bytes(data)?.as_bytes())
            .map(|()| true)
            .or_else(|e| match e {
                core_queue::Error::Full => Ok(false),
                e => Err(e),
            })
            .map_err(error)
    }
    fn try_send_batch(&self, messages: Vec<Bound<'_, PyAny>>) -> PyResult<usize> {
        let messages = messages.iter().map(bytes).collect::<PyResult<Vec<_>>>()?;
        let slices = messages.iter().map(|m| m.as_bytes()).collect::<Vec<_>>();
        self.inner
            .as_ref()
            .ok_or_else(closed)?
            .try_send_batch(&slices)
            .map_err(error)
    }
    fn close(&mut self) {
        self.inner.take();
    }
    fn __enter__(slf: PyRef<'_, Self>) -> PyRef<'_, Self> {
        slf
    }
    fn __exit__(
        &mut self,
        _kind: &Bound<'_, PyAny>,
        _value: &Bound<'_, PyAny>,
        _traceback: &Bound<'_, PyAny>,
    ) {
        self.close();
    }
}

#[pyclass(frozen, module = "cloudtoid_interprocess._native")]
struct Subscriber {
    inner: RwLock<Option<core_queue::Subscriber>>,
}
#[pymethods]
impl Subscriber {
    #[new]
    #[pyo3(signature = (name, capacity, path=None))]
    fn new(name: String, capacity: usize, path: Option<PathBuf>) -> PyResult<Self> {
        Ok(Self {
            inner: RwLock::new(Some(
                core_queue::Subscriber::open(&options(name, capacity, path)).map_err(error)?,
            )),
        })
    }
    fn try_receive<'py>(&self, py: Python<'py>) -> PyResult<Option<Bound<'py, PyBytes>>> {
        Ok(self
            .inner
            .read()
            .unwrap()
            .as_ref()
            .ok_or_else(closed)?
            .try_recv()
            .map_err(error)?
            .map(|m| PyBytes::new(py, &m)))
    }
    #[pyo3(signature = (timeout=None))]
    fn receive<'py>(
        &self,
        py: Python<'py>,
        timeout: Option<f64>,
    ) -> PyResult<Option<Bound<'py, PyBytes>>> {
        let timeout = timeout
            .map(|seconds| {
                Duration::try_from_secs_f64(seconds)
                    .map_err(|_| PyValueError::new_err("timeout must be finite and nonnegative"))
            })
            .transpose()?;
        let started = Instant::now();
        loop {
            py.check_signals()?;
            let wait = timeout.map_or(Duration::from_millis(100), |limit| {
                limit
                    .saturating_sub(started.elapsed())
                    .min(Duration::from_millis(100))
            });
            let message = py.detach(|| {
                let guard = self.inner.read().unwrap();
                guard
                    .as_ref()
                    .ok_or_else(closed)?
                    .recv_timeout(wait)
                    .map_err(error)
            })?;
            if let Some(message) = message {
                return Ok(Some(PyBytes::new(py, &message)));
            }
            // Do not discard an already-consumed message to report a Python signal.
            py.check_signals()?;
            if timeout.is_some_and(|limit| started.elapsed() >= limit) {
                return Ok(None);
            }
        }
    }
    fn close(&self) {
        self.inner.write().unwrap().take();
    }
    fn __enter__(slf: PyRef<'_, Self>) -> PyRef<'_, Self> {
        slf
    }
    fn __exit__(
        &self,
        _kind: &Bound<'_, PyAny>,
        _value: &Bound<'_, PyAny>,
        _traceback: &Bound<'_, PyAny>,
    ) {
        self.close();
    }
}
#[pymodule]
fn _native(m: &Bound<'_, PyModule>) -> PyResult<()> {
    m.add_class::<Publisher>()?;
    m.add_class::<Subscriber>()?;
    m.add(
        "CapacityMismatchError",
        m.py().get_type::<CapacityMismatchError>(),
    )?;
    m.add(
        "PublisherLimitError",
        m.py().get_type::<PublisherLimitError>(),
    )?;
    m.add("CorruptQueueError", m.py().get_type::<CorruptQueueError>())?;
    Ok(())
}
