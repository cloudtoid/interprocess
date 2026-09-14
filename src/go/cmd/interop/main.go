package main

import (
	"bytes"
	"encoding/binary"
	"fmt"
	queue "github.com/cloudtoid/interprocess/src/go/v3"
	"os"
	"runtime"
	"strconv"
	"time"
)

func check(err error) {
	if err != nil {
		panic(err)
	}
}
func message(i int) []byte {
	data := make([]byte, 8+i%251)
	binary.LittleEndian.PutUint64(data, uint64(i))
	for j := 8; j < len(data); j++ {
		data[j] = byte((i + j) % 251)
	}
	return data
}
func main() {
	options := queue.Options{Name: os.Args[2], Path: os.Args[3], Capacity: 4096}
	count, err := strconv.Atoi(os.Args[4])
	check(err)
	if os.Args[1] == "publish" {
		p, err := queue.OpenPublisher(options)
		check(err)
		defer p.Close()
		for i := 0; i < count; i++ {
			data := message(i)
			for {
				ok, err := p.TrySend(data)
				check(err)
				if ok {
					break
				}
				runtime.Gosched()
			}
		}
	} else {
		s, err := queue.OpenSubscriber(options)
		check(err)
		defer s.Close()
		fmt.Println("READY")
		for i := 0; i < count; i++ {
			data, err := s.Receive(30 * time.Second)
			check(err)
			if !bytes.Equal(data, message(i)) {
				panic(fmt.Sprintf("message %d differs", i))
			}
		}
	}
}
