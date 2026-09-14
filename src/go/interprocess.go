// Package interprocess provides shared-memory byte queues interoperable with
// Cloudtoid protocol v3. Install the C SDK before building this cgo package.
package interprocess

/*
#cgo pkg-config: cloudtoid-interprocess
#include <interprocess.h>
#include <stdlib.h>
#include <string.h>
// Copy errors before returning across cgo: a goroutine can resume on a different
// OS thread, where the native thread-local error would no longer be its own.
typedef struct { int32_t status; int32_t kind; char *error; } cip_result;
static cip_result cip_result_for(int32_t status) {
    cip_result r = {status, cip_last_error_kind(), NULL};
    if (status < 0) {
        const char *message = cip_last_error();
        size_t size = strlen(message) + 1;
        r.error = (char*)malloc(size);
        if (r.error) memcpy(r.error, message, size);
    }
    return r;
}
static cip_result go_pub_open(const char *name, const char *path, size_t capacity, cip_publisher **out) {
    return cip_result_for(cip_publisher_open(name, path, capacity, out));
}
static cip_result go_sub_open(const char *name, const char *path, size_t capacity, cip_subscriber **out) {
    return cip_result_for(cip_subscriber_open(name, path, capacity, out));
}
static cip_result go_send(cip_publisher *p, const uint8_t *data, size_t len) {
    return cip_result_for(cip_try_send(p, data, len));
}
static cip_result go_receive(cip_subscriber *s, int64_t timeout, cip_buffer *out) {
    return cip_result_for(cip_receive(s, timeout, out));
}
static cip_result go_receive_into(cip_subscriber *s, uint8_t *data, size_t len, size_t *copied) {
    return cip_result_for(cip_try_receive_into(s, data, len, copied));
}
*/
import "C"
import (
	"context"
	"errors"
	"fmt"
	"strings"
	"sync"
	"time"
	"unsafe"
)

// Options identify a queue. Path defaults to the OS temp directory; Windows
// ignores Path. Capacity must match every participant, exceed 16, and divide by 8.
type Options struct {
	Name, Path string
	Capacity   int
}

// ErrClosed reports an operation on a closed endpoint.
var ErrClosed = errors.New("interprocess: endpoint is closed")

// Stable native error kinds support errors.Is across the C boundary.
var (
	ErrInvalidArgument  = errors.New("interprocess: invalid argument")
	ErrCapacityMismatch = errors.New("interprocess: capacity mismatch")
	ErrPublisherLimit   = errors.New("interprocess: publisher limit reached")
	ErrExhausted        = errors.New("interprocess: queue lifetime exhausted")
	ErrCorrupt          = errors.New("interprocess: corrupt queue")
	ErrIO               = errors.New("interprocess: operating system error")
	ErrInternal         = errors.New("interprocess: internal error")
)

func result(r C.cip_result) (bool, error) {
	if r.status >= 0 {
		return r.status == 1, nil
	}
	if r.error == nil {
		return false, errors.New("interprocess: native operation failed")
	}
	defer C.free(unsafe.Pointer(r.error))
	kind := ErrInternal
	switch r.kind {
	case C.CIP_INVALID_ARGUMENT:
		kind = ErrInvalidArgument
	case C.CIP_CAPACITY_MISMATCH:
		kind = ErrCapacityMismatch
	case C.CIP_PUBLISHER_LIMIT:
		kind = ErrPublisherLimit
	case C.CIP_EXHAUSTED:
		kind = ErrExhausted
	case C.CIP_CORRUPT:
		kind = ErrCorrupt
	case C.CIP_IO_ERROR:
		kind = ErrIO
	}
	return false, fmt.Errorf("%w: %s", kind, C.GoString(r.error))
}
func stringsFor(o Options) (*C.char, *C.char, func(), error) {
	if o.Capacity <= 16 || o.Capacity%8 != 0 {
		return nil, nil, nil, ErrInvalidArgument
	}
	if strings.IndexByte(o.Name, 0) >= 0 || strings.IndexByte(o.Path, 0) >= 0 {
		return nil, nil, nil, ErrInvalidArgument
	}
	name := C.CString(o.Name)
	var path *C.char
	if o.Path != "" {
		path = C.CString(o.Path)
	}
	return name, path, func() { C.free(unsafe.Pointer(name)); C.free(unsafe.Pointer(path)) }, nil
}

// Publisher supports concurrent sends. Do not copy an endpoint after first use.
// Close releases its queue registration; always defer Close after opening.
type Publisher struct {
	mu     sync.RWMutex
	handle *C.cip_publisher
}

// OpenPublisher creates or joins a transient queue as a publisher.
func OpenPublisher(o Options) (*Publisher, error) {
	name, path, free, err := stringsFor(o)
	if err != nil {
		return nil, err
	}
	defer free()
	p := &Publisher{}
	_, err = result(C.go_pub_open(name, path, C.size_t(o.Capacity), &p.handle))
	if err != nil {
		return nil, err
	}
	return p, nil
}

// TrySend publishes without waiting; false means full or recovering.
func (p *Publisher) TrySend(message []byte) (bool, error) {
	p.mu.RLock()
	defer p.mu.RUnlock()
	if p.handle == nil {
		return false, ErrClosed
	}
	return result(C.go_send(p.handle, (*C.uint8_t)(unsafe.Pointer(unsafe.SliceData(message))), C.size_t(len(message))))
}

// Close releases the publisher and is safe to call repeatedly.
func (p *Publisher) Close() error {
	p.mu.Lock()
	defer p.mu.Unlock()
	C.cip_publisher_close(p.handle)
	p.handle = nil
	return nil
}

// Subscriber competes with other subscribers for messages. Close waits for the
// current native call, then interrupts outstanding Receive calls.
type Subscriber struct {
	mu     sync.RWMutex
	handle *C.cip_subscriber
}

// OpenSubscriber creates or joins a transient queue as a subscriber.
func OpenSubscriber(o Options) (*Subscriber, error) {
	name, path, free, err := stringsFor(o)
	if err != nil {
		return nil, err
	}
	defer free()
	s := &Subscriber{}
	_, err = result(C.go_sub_open(name, path, C.size_t(o.Capacity), &s.handle))
	if err != nil {
		return nil, err
	}
	return s, nil
}

// Receive waits for a message, context cancellation, or Close. Cancellation returns
// ctx.Err(). Use context.Background() to wait without a deadline.
func (s *Subscriber) Receive(ctx context.Context) ([]byte, error) {
	var timer *time.Timer
	for {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		message, err := s.TryReceive()
		if message != nil || err != nil {
			return message, err
		}
		if timer == nil {
			timer = time.NewTimer(time.Millisecond)
			defer timer.Stop()
		} else {
			timer.Reset(time.Millisecond)
		}
		select {
		case <-ctx.Done():
			return nil, ctx.Err()
		case <-timer.C:
		}
	}
}

// TryReceive returns (nil, nil) when empty. Empty messages return a non-nil slice.
// The native call is nonblocking; waiting uses Go timers without pinning OS threads.
func (s *Subscriber) TryReceive() ([]byte, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if s.handle == nil {
		return nil, ErrClosed
	}
	var buffer C.cip_buffer
	ok, err := result(C.go_receive(s.handle, 0, &buffer))
	if err != nil || !ok {
		return nil, err
	}
	defer C.cip_buffer_free(buffer)
	message := make([]byte, int(buffer.length))
	copy(message, unsafe.Slice((*byte)(unsafe.Pointer(buffer.data)), len(message)))
	return message, nil
}

// TryReceiveInto truncates AND consumes messages larger than buffer. Its bool
// distinguishes an empty queue from a successfully received zero-byte message.
func (s *Subscriber) TryReceiveInto(buffer []byte) (int, bool, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if s.handle == nil {
		return 0, false, ErrClosed
	}
	var copied C.size_t
	ok, err := result(C.go_receive_into(s.handle, (*C.uint8_t)(unsafe.Pointer(unsafe.SliceData(buffer))), C.size_t(len(buffer)), &copied))
	return int(copied), ok, err
}

// Close releases the subscriber; pending Receive calls observe ErrClosed.
func (s *Subscriber) Close() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	C.cip_subscriber_close(s.handle)
	s.handle = nil
	return nil
}
