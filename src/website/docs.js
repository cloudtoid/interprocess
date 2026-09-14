document.querySelectorAll('.doc-content pre').forEach((pre, index) => {
  const code = pre.querySelector('code');
  if (!code) return;
  if (window.hljs) hljs.highlightElement(code);
  const wrapper = document.createElement('div');
  wrapper.className = 'doc-code';
  pre.before(wrapper);
  wrapper.append(pre);
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'doc-copy';
  button.textContent = 'Copy';
  button.setAttribute('aria-label', `Copy code example ${index + 1}`);
  const status = document.createElement('span');
  status.className = 'sr-only';
  status.setAttribute('role', 'status');
  wrapper.append(button, status);
  button.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(code.textContent);
      button.textContent = 'Copied';
      status.textContent = 'Code copied to clipboard';
    } catch {
      button.textContent = 'Select to copy';
      status.textContent = 'Clipboard unavailable. Select the code to copy it.';
    }
  });
});
