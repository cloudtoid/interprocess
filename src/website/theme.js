(() => {
  const system = window.matchMedia('(prefers-color-scheme: dark)');
  let preference = 'system';
  const palettes = ['iris', 'cobalt', 'rose', 'terracotta', 'plum', 'teal', 'olive'];
  const palette = palettes[Math.floor(Math.random() * palettes.length)];
  try {
    const saved = localStorage.getItem('cloudtoid-theme');
    if (saved === 'light' || saved === 'dark') preference = saved;
  } catch { /* System preference still works when storage is unavailable. */ }
  function apply() {
    document.documentElement.dataset.palette = palette;
    document.documentElement.dataset.theme = preference === 'system'
      ? (system.matches ? 'dark' : 'light') : preference;
  }
  apply();
  system.addEventListener('change', apply);
  document.addEventListener('DOMContentLoaded', () => {
    const control = document.querySelector('#theme');
    control.value = preference;
    control.addEventListener('change', () => {
      preference = control.value;
      try { localStorage.setItem('cloudtoid-theme', preference); } catch { }
      apply();
    });
  });
})();
