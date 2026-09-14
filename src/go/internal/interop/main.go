package main

import (
	"bufio"
	"bytes"
	"context"
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
	length := 8 + i%251
	if i%251 == 250 {
		length = 4088
	}
	data := make([]byte, length)
	binary.LittleEndian.PutUint64(data, uint64(i))
	for j := 8; j < len(data); j++ {
		data[j] = byte((i + j) % 251)
	}
	return data
}
func main() {
	options := queue.Options{Name: os.Args[2], Path: os.Args[3], Capacity: 4096}
	if value := os.Getenv("INTEROP_CAPACITY"); value != "" {
		capacity, err := strconv.Atoi(value)
		check(err)
		options.Capacity = capacity
	}
	count, err := strconv.Atoi(os.Args[4])
	check(err)
	if os.Args[1] == "publish" {
		p, err := queue.OpenPublisher(options)
		check(err)
		defer p.Close()
		start := 0
		if len(os.Args) > 5 {
			start, err = strconv.Atoi(os.Args[5])
			check(err)
			fmt.Println("READY")
			_, err = bufio.NewReader(os.Stdin).ReadString('\n')
			check(err)
		}
		for i := start; i < start+count; i++ {
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
		ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
		defer cancel()
		if os.Args[1] == "collect" {
			for {
				data, err := s.Receive(ctx)
				check(err)
				if data == nil {
					panic("collector timed out")
				}
				if len(data) == 0 {
					break
				}
				id := int(binary.LittleEndian.Uint64(data))
				if !bytes.Equal(data, message(id)) {
					panic("message differs")
				}
				fmt.Println(id)
			}
			return
		}
		for i := 0; i < count; i++ {
			data, err := s.Receive(ctx)
			check(err)
			if !bytes.Equal(data, message(i)) {
				panic(fmt.Sprintf("message %d differs", i))
			}
		}
	}
}
