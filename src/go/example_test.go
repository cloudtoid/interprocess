package interprocess_test

import (
	"fmt"
	"github.com/cloudtoid/interprocess/src/go/v3"
	"os"
)

func Example() {
	options := interprocess.Options{Name: fmt.Sprintf("example%d", os.Getpid()), Capacity: 65536}
	subscriber, err := interprocess.OpenSubscriber(options)
	if err != nil {
		panic(err)
	}
	defer subscriber.Close()
	publisher, err := interprocess.OpenPublisher(options)
	if err != nil {
		panic(err)
	}
	defer publisher.Close()
	sent, err := publisher.TrySend([]byte("hello"))
	if err != nil {
		panic(err)
	}
	if sent {
		message, err := subscriber.TryReceive()
		if err != nil {
			panic(err)
		}
		fmt.Println(string(message))
	}
	// Output: hello
}
