const installation = {
  "Rust": [
    "Run in your Cargo project. Requires Rust 1.87 or later.",
    "cargo add cloudtoid-interprocess"
  ],
  "Node.js": [
    "Run in your Node.js project. Requires Node.js 18 or later; platform binaries install automatically.",
    "npm install @cloudtoid/interprocess"
  ],
  ".NET": [
    "Run in your .NET project. Requires .NET 10 or later and a 64-bit process.",
    "dotnet add package Cloudtoid.Interprocess"
  ],
  "Python": [
    "PyPI publishing is pending. Run in an activated Python 3.9+ virtual environment with Git, Rust, and a native linker installed.",
    "python -m pip install \"git+https://github.com/cloudtoid/interprocess.git@native-v3.0.1#subdirectory=src/python\""
  ],
  "Go": [
    "Install the C SDK first (see the C guide), then run in your Go module. Requires Go 1.24+, cgo enabled, a C compiler, and pkg-config.",
    "go get github.com/cloudtoid/interprocess/src/go/v3@latest"
  ],
  "C": [
    "macOS Apple Silicon example, using the GitHub CLI. For other platforms, choose darwin-x64, linux-arm64, linux-x64, or win32-x64 in both archive names. See the C guide for Windows setup.",
    "gh release download native-v3.0.1 --repo cloudtoid/interprocess --pattern \"*-darwin-arm64.tar.gz\"\nmkdir -p cloudtoid-sdk\ntar -xzf cloudtoid-interprocess-3.0.1-darwin-arm64.tar.gz -C cloudtoid-sdk --strip-components=1\nexport PKG_CONFIG_PATH=\"$PWD/cloudtoid-sdk/lib/pkgconfig:$PKG_CONFIG_PATH\""
  ]
};
const examples = {
  Rust: ['rust', `let options = Options::new("work", 65536);
let subscriber = Subscriber::open(&options)?;
let publisher = Publisher::open(&options)?;

publisher.try_send(b"hello")?;
let message = subscriber.try_recv()?;`],
  Python: ['python', `from cloudtoid_interprocess import Publisher, Subscriber

with Subscriber("work", 65536) as subscriber:
    with Publisher("work", 65536) as publisher:
        if publisher.try_send(b"hello"):
            message = subscriber.receive(timeout=1.0)`],
  'Node.js': ['node', `import { Publisher, Subscriber } from '@cloudtoid/interprocess';
const subscriber = new Subscriber('work', 65536);
const publisher = new Publisher('work', 65536);
try {
  if (publisher.trySend(Buffer.from('hello'))) {
    const message = await subscriber.receive({
      signal: AbortSignal.timeout(1000)
    });
  }
} finally { publisher.close(); subscriber.close(); }`],
  Go: ['go', `options := interprocess.Options{Name: "work", Capacity: 65536}
subscriber, err := interprocess.OpenSubscriber(options)
if err != nil { panic(err) }
defer subscriber.Close()
publisher, err := interprocess.OpenPublisher(options)
if err != nil { panic(err) }
defer publisher.Close()
if sent, err := publisher.TrySend([]byte("hello")); err != nil {
    panic(err)
} else if sent {
    message, err := subscriber.TryReceive()
    fmt.Println(string(message), err)
}`],
  C: ['c', `cip_subscriber *subscriber = NULL;
cip_publisher *publisher = NULL;
cip_subscriber_open("work", NULL, 65536, &subscriber);
cip_publisher_open("work", NULL, 65536, &publisher);
if (cip_try_send(publisher, (uint8_t*)"hello", 5) == 1) {
    cip_buffer message;
    if (cip_receive(subscriber, 1000, &message) == 1)
        cip_buffer_free(message);
}
cip_publisher_close(publisher);
cip_subscriber_close(subscriber);`],
  '.NET': ['dotnet', `var factory = new QueueFactory();
var options = new QueueOptions("work", 65536);
using var subscriber = factory.CreateSubscriber(options);
using var publisher = factory.CreatePublisher(options);

if (publisher.TryEnqueue("hello"u8)) {
    subscriber.TryDequeue(out var message);
}`]
};
const tabs = [...document.querySelectorAll('[role=tab]')];
const code = document.querySelector('#example code');
function select(tab, focus = false) {
  tabs.forEach(t => { t.setAttribute('aria-selected', String(t === tab)); t.tabIndex = t === tab ? 0 : -1; });
  const language = tab.dataset.language, [folder, source] = examples[language];
  document.querySelector('#install-note').textContent = installation[language][0];
  document.querySelector('#install-command').textContent = installation[language][1];
  const grammar = { node: 'javascript', dotnet: 'csharp' }[folder] || folder;
  code.innerHTML = hljs.highlight(source, { language: grammar }).value;
  document.querySelector('#example').setAttribute('aria-label', `${language} example`);
  const link = document.querySelector('#language-docs');
  link.href = `https://github.com/cloudtoid/interprocess/tree/main/src/${folder}`;
  link.textContent = `Read the ${language} guide →`;
  document.querySelector('#copy-status').textContent = '';
  if (focus) tab.focus();
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => select(tab));
  tab.addEventListener('keydown', event => {
    let target;
    if (event.key === 'ArrowRight') target = (index + 1) % tabs.length;
    if (event.key === 'ArrowLeft') target = (index + tabs.length - 1) % tabs.length;
    if (event.key === 'Home') target = 0;
    if (event.key === 'End') target = tabs.length - 1;
    if (target !== undefined) { event.preventDefault(); select(tabs[target], true); }
  });
});
document.querySelector('#copy').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText(code.textContent); document.querySelector('#copy-status').textContent = 'Copied'; }
  catch { document.querySelector('#copy-status').textContent = 'Select the code to copy'; }
});
select(tabs[0]);
