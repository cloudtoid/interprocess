package interprocess

import (
	"bytes"
	"context"
	"errors"
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
	ctx, cancel := context.WithTimeout(context.Background(), time.Millisecond)
	defer cancel()
	if got, err := s.Receive(ctx); got != nil || !errors.Is(err, context.DeadlineExceeded) {
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

func TestReceiveCancellationAndClose(t *testing.T) {
	options := Options{Name: fmt.Sprintf("gc%d", os.Getpid()), Capacity: 64}
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
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	p.TrySend([]byte("keep"))
	if _, err := s.Receive(ctx); !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	if data, err := s.TryReceive(); err != nil || string(data) != "keep" {
		t.Fatal(data, err)
	}

	ctx, cancel = context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { _, err := s.Receive(ctx); done <- err }()
	time.Sleep(10 * time.Millisecond)
	cancel()
	select {
	case err := <-done:
		if !errors.Is(err, context.Canceled) {
			t.Fatal(err)
		}
	case <-time.After(time.Second):
		t.Fatal("cancelled receive did not stop")
	}

	go func() { _, err := s.Receive(context.Background()); done <- err }()
	time.Sleep(10 * time.Millisecond)
	closed := make(chan struct{})
	go func() { s.Close(); close(closed) }()
	select {
	case <-closed:
	case <-time.After(time.Second):
		t.Fatal("Close blocked on an indefinite receive")
	}
	select {
	case err := <-done:
		if !errors.Is(err, ErrClosed) {
			t.Fatal(err)
		}
	case <-time.After(time.Second):
		t.Fatal("receive survived Close")
	}
}
