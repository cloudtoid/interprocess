package interprocess

import (
	"bytes"
	"fmt"
	"os"
	"sync"
	"testing"
	"time"
)

func TestLifetimeAndMessages(t *testing.T) {
	options := Options{Name: fmt.Sprintf("g%d", os.Getpid()), Capacity: 64}
	p, err := OpenPublisher(options)
	if err != nil {
		t.Fatal(err)
	}
	defer p.Close()
	s, err := OpenSubscriber(options)
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	for _, data := range [][]byte{{}, {1, 2, 3}, bytes.Repeat([]byte{9}, 56)} {
		ok, err := p.TrySend(data)
		if err != nil || !ok {
			t.Fatalf("send %v %v", ok, err)
		}
		got, err := s.TryReceive()
		if err != nil || got == nil || !bytes.Equal(got, data) {
			t.Fatalf("receive %v %v", got, err)
		}
	}
	if got, err := s.Receive(time.Millisecond); got != nil || err != nil {
		t.Fatalf("timeout %v %v", got, err)
	}
	p.TrySend([]byte{1, 2, 3})
	buf := make([]byte, 2)
	if n, ok, err := s.TryReceiveInto(buf); n != 2 || !ok || err != nil || !bytes.Equal(buf, []byte{1, 2}) {
		t.Fatal(n, ok, err, buf)
	}
	var calls sync.WaitGroup
	calls.Add(1)
	go func() {
		defer calls.Done()
		for i := 0; i < 1000; i++ {
			p.TrySend([]byte{1})
		}
	}()
	p.Close()
	calls.Wait()
	if _, err := p.TrySend(nil); err != ErrClosed {
		t.Fatal(err)
	}
}
