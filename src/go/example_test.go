package interprocess_test

import (
	"fmt"
	"github.com/cloudtoid/interprocess/src/go/v3"
)

func Example() {
	options := interprocess.Options{Name: "go-example", Capacity: 65536}
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
