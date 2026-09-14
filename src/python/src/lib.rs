use core_queue::Options;
use pyo3::{
    exceptions::{PyRuntimeError, PyValueError},
    prelude::*,
    types::PyBytes,
};
use std::time::{Duration, Instant};

fn error(e: impl std::fmt::Display) -> PyErr {
    PyRuntimeError::new_err(e.to_string())
}
fn options(name: String, capacity: usize, path: Option<String>) -> Options {
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
    fn new(name: String, capacity: usize, path: Option<String>) -> PyResult<Self> {
        Ok(Self {
            inner: Some(core_queue::Publisher::open(options(name, capacity, path)).map_err(error)?),
        })
    }
    fn try_send(&self, data: &Bound<'_, PyBytes>) -> PyResult<bool> {
        self.inner
            .as_ref()
            .ok_or_else(|| error("publisher is closed"))?
            .try_send(data.as_bytes())
            .map_err(error)
    }
    fn try_send_batch(&self, messages: Vec<Bound<'_, PyBytes>>) -> PyResult<usize> {
        let slices = messages.iter().map(|m| m.as_bytes()).collect::<Vec<_>>();
        self.inner
            .as_ref()
            .ok_or_else(|| error("publisher is closed"))?
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

#[pyclass(module = "cloudtoid_interprocess._native")]
struct Subscriber {
    inner: Option<core_queue::Subscriber>,
}
#[pymethods]
impl Subscriber {
    #[new]
    #[pyo3(signature = (name, capacity, path=None))]
    fn new(name: String, capacity: usize, path: Option<String>) -> PyResult<Self> {
        Ok(Self {
            inner: Some(
                core_queue::Subscriber::open(options(name, capacity, path)).map_err(error)?,
            ),
        })
    }
    fn try_receive<'py>(&self, py: Python<'py>) -> PyResult<Option<Bound<'py, PyBytes>>> {
        Ok(self
            .inner
            .as_ref()
            .ok_or_else(|| error("subscriber is closed"))?
            .try_receive()
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
        let subscriber = self
            .inner
            .as_ref()
            .ok_or_else(|| error("subscriber is closed"))?;
        let started = Instant::now();
        loop {
            let wait = timeout.map_or(Duration::from_millis(100), |limit| {
                limit
                    .saturating_sub(started.elapsed())
                    .min(Duration::from_millis(100))
            });
            let message = py
                .detach(|| subscriber.receive_timeout(wait))
                .map_err(error)?;
            py.check_signals()?;
            if let Some(message) = message {
                return Ok(Some(PyBytes::new(py, &message)));
            }
            if timeout.is_some_and(|limit| started.elapsed() >= limit) {
                return Ok(None);
            }
        }
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
#[pymodule]
fn _native(m: &Bound<'_, PyModule>) -> PyResult<()> {
    m.add_class::<Publisher>()?;
    m.add_class::<Subscriber>()?;
    Ok(())
}
