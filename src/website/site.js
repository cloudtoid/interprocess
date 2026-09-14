const examples = {
  Rust: ['rust', `let options = Options::new("work", 65536);
let subscriber = Subscriber::open(options.clone())?;
let publisher = Publisher::open(options)?;

if publisher.try_send(b"hello")? {
    let message = subscriber.try_receive()?;
}`],
  Python: ['python', `from cloudtoid_interprocess import Publisher, Subscriber

with Subscriber("work", 65536) as subscriber:
    with Publisher("work", 65536) as publisher:
        if publisher.try_send(b"hello"):
            message = subscriber.receive(timeout=1.0)`],
  'Node.js': ['node', `import queue from '@cloudtoid/interprocess';
const { Publisher, Subscriber } = queue;
const subscriber = new Subscriber('work', 65536);
const publisher = new Publisher('work', 65536);
try {
  if (publisher.trySend(Buffer.from('hello'))) {
    const message = await subscriber.receive({
      signal: AbortSignal.timeout(1000)
    });
  }
} finally { publisher.close(); subscriber.close(); }`],
  Go: ['go', `options := queue.Options{Name: "work", Capacity: 65536}
subscriber, err := queue.OpenSubscriber(options)
if err != nil { panic(err) }
defer subscriber.Close()
publisher, err := queue.OpenPublisher(options)
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
  const grammar = { node: 'javascript', dotnet: 'csharp' }[folder] || folder;
  code.innerHTML = hljs.highlight(source, { language: grammar }).value;
  document.querySelector('#example').setAttribute('aria-label', `${language} example`);
  const link = document.querySelector('#language-docs');
  link.href = `https://github.com/cloudtoid/interprocess/tree/aa44640ef0941d0349322c6c57a496245b8a0b79/src/${folder}`;
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
