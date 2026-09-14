(() => {
  const demo = document.getElementById('queue-demo');
  if (!demo) return;
  const capacity = 64;
  let read = 0, write = 0, nextId = 1;
  let records = [];
  const cells = demo.querySelector('#queue-cells');
  const status = demo.querySelector('#queue-status');
  const pause = demo.querySelector('#queue-pause');
  const size = demo.querySelector('#queue-size');
  const finish = demo.querySelector('#queue-finish');

  function render(message) {
    cells.replaceChildren();
    for (let offset = 0; offset < capacity; offset += 8) {
      const record = records.find(item => (offset - item.start % capacity + capacity) % capacity < item.length);
      const cell = document.createElement('div');
      cell.className = 'queue-cell';
      const label = document.createElement('span');
      label.className = 'queue-byte';
      label.textContent = `Byte ${offset}`;
      const content = document.createElement('strong');
      content.textContent = 'Free';
      if (record) {
        const header = offset === record.start % capacity;
        cell.dataset.publisher = record.publisher;
        cell.dataset.ready = String(record.ready);
        content.textContent = `${record.publisher}${record.id} ${header ? 'H' : 'P'}`;
        cell.title = `Message ${record.publisher}${record.id}: ${header ? 'header' : 'payload'}, ${record.ready ? 'ready' : 'unfinished'}`;
      }
      const marker = document.createElement('small');
      marker.textContent = [offset === read % capacity ? 'R' : '', offset === write % capacity ? 'W' : ''].filter(Boolean).join(' / ');
      cell.append(label, content, marker);
      cells.append(cell);
    }
    demo.querySelector('#queue-read').textContent = String(read);
    demo.querySelector('#queue-write').textContent = String(write);
    demo.querySelector('#queue-used').textContent = `${write - read} / ${capacity} B`;
    finish.disabled = !records.some(record => !record.ready);
    status.textContent = message;
  }

  demo.querySelectorAll('[data-publish]').forEach(button => button.addEventListener('click', () => {
    const payload = Number(size.value);
    const length = Math.ceil((payload + 8) / 8) * 8;
    if (write - read + length > capacity) {
      render(`Full for this message: ${length} bytes needed, ${capacity - (write - read)} available. Nothing was reserved.`);
      return;
    }
    const record = { id: nextId++, publisher: button.dataset.publish, start: write, length, ready: !pause.checked };
    records.push(record);
    write += length;
    const wrapped = record.start % capacity + length > capacity;
    render(`${record.publisher}${record.id} reserved ${length} bytes (${payload} payload + 8 header)${wrapped ? ' across the physical end' : ''}. ${record.ready ? 'State 2: ready to receive in order.' : 'State 0: publication paused; the reservation stays occupied.'}`);
  }));

  finish.addEventListener('click', () => {
    const record = records.find(item => !item.ready);
    if (!record) return;
    record.ready = true;
    render(`${record.publisher}${record.id} published state 2 after writing its payload and length. ReadOffset and WriteOffset did not change.`);
  });

  demo.querySelector('#queue-receive').addEventListener('click', () => {
    const record = records[0];
    if (!record) { render('Empty queue. No message to receive.'); return; }
    if (!record.ready) {
      render(`${record.publisher}${record.id} is unfinished. Later ready messages cannot pass it. A paused live publisher is not expired by a timeout.`);
      return;
    }
    records.shift();
    read += record.length;
    render(`Received ${record.publisher}${record.id}: claimed state 2 → 1, copied payload, cleared the record, then advanced ReadOffset by ${record.length}. That space is reusable.`);
  });

  demo.querySelector('#queue-reset').addEventListener('click', () => {
    read = write = 0;
    nextId = 1;
    records = [];
    pause.checked = false;
    render('Fresh demonstration queue. Real counters never reset while a queue is live.');
  });
  demo.hidden = false;
  render('Empty queue. R and W mark physical read and write positions; both start at byte 0.');
})();
