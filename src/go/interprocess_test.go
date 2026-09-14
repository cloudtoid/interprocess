package interprocess

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"os"
	"runtime"
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
	if _, err := p.TrySend(nil); !errors.Is(err, ErrClosed) {
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

func TestErrorKindsAndConcurrentReceivers(t *testing.T) {
	options := Options{Name: fmt.Sprintf("gm%d", os.Getpid()), Capacity: 8192}
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
	bad := options
	bad.Capacity *= 2
	if _, err := OpenPublisher(bad); !errors.Is(err, ErrCapacityMismatch) {
		t.Fatal(err)
	}
	bad.Name = "bad/name"
	if _, err := OpenSubscriber(bad); !errors.Is(err, ErrInvalidArgument) {
		t.Fatal(err)
	}
	const count = 64
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	received := make(chan []byte, count)
	failures := make(chan error, count)
	for i := 0; i < count; i++ {
		go func() {
			message, err := s.Receive(ctx)
			if err != nil {
				failures <- err
			} else {
				received <- message
			}
		}()
	}
	time.Sleep(20 * time.Millisecond)
	for i := 0; i < count; i++ {
		if ok, err := p.TrySend([]byte{byte(i)}); !ok || err != nil {
			t.Fatal(ok, err)
		}
	}
	seen := make(map[byte]bool)
	for i := 0; i < count; i++ {
		select {
		case message := <-received:
			if len(message) != 1 || seen[message[0]] {
				t.Fatal(message)
			}
			seen[message[0]] = true
		case err := <-failures:
			t.Fatal(err)
		case <-ctx.Done():
			t.Fatal(ctx.Err())
		}
	}
	if ok, err := p.TrySend(nil); !ok || err != nil {
		t.Fatal(ok, err)
	}
	if msg, err := s.Receive(ctx); msg == nil || len(msg) != 0 || err != nil {
		t.Fatal(msg, err)
	}
	s.Close()
	if _, _, err := s.TryReceiveInto(make([]byte, 8)); !errors.Is(err, ErrClosed) {
		t.Fatal(err)
	}
}

func TestIdleReceiveBackoff(t *testing.T) {
	s, err := OpenSubscriber(Options{Name: fmt.Sprintf("idle%d", os.Getpid()), Capacity: 64})
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	const waiters = 64
	ctx, cancel := context.WithTimeout(context.Background(), 250*time.Millisecond)
	defer cancel()
	var group sync.WaitGroup
	failures := make(chan error, waiters)
	before, started := runtime.NumCgoCall(), time.Now()
	for i := 0; i < waiters; i++ {
		group.Add(1)
		go func() {
			defer group.Done()
			_, err := s.Receive(ctx)
			if !errors.Is(err, context.DeadlineExceeded) {
				failures <- err
			}
		}()
	}
	group.Wait()
	close(failures)
	for err := range failures {
		t.Fatal(err)
	}
	elapsed := time.Since(started)
	calls := runtime.NumCgoCall() - before
	// Allows scheduling noise and startup retries, but rejects sustained 1 ms polling.
	if calls > int64(waiters)*(int64(elapsed/(5*time.Millisecond))+10) {
		t.Fatalf("%d idle cgo calls in %v", calls, elapsed)
	}
}
