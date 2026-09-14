#include <interprocess.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static void check(int status) { if (status < 0) { fprintf(stderr, "%s\n", cip_last_error()); exit(1); } }
static size_t message(unsigned i, unsigned char *data) {
    size_t length = i % 251 == 250 ? 4088 : 8 + i % 251;
    for (unsigned j = 0; j < 8; j++) data[j] = (uint8_t)((uint64_t)i >> (8*j));
    for (size_t j = 8; j < length; j++) data[j] = (i+j)%251;
    return length;
}
int main(int argc, char **argv) {
    if (argc != 5 && argc != 6) return 2;
    unsigned count = (unsigned)strtoul(argv[4], NULL, 10);
    unsigned char expected[4088];
    const char *configured = getenv("INTEROP_CAPACITY");
    size_t capacity = configured ? (size_t)strtoull(configured, NULL, 10) : 4096;
    if (strcmp(argv[1], "publish") == 0) {
        cip_publisher *p = NULL;
        check(cip_publisher_open(argv[2], argv[3], capacity, &p));
        unsigned start = argc == 6 ? (unsigned)strtoul(argv[5], NULL, 10) : 0;
        if (argc == 6) { puts("READY"); fflush(stdout); getchar(); }
        for (unsigned i = start; i < start + count; i++) {
            size_t length = message(i, expected);
            int status;
            do { status = cip_try_send(p, expected, length); check(status); } while (!status);
        }
        cip_publisher_close(p);
    } else {
        cip_subscriber *s = NULL;
        check(cip_subscriber_open(argv[2], argv[3], capacity, &s));
        puts("READY"); fflush(stdout);
        if (strcmp(argv[1], "collect") == 0) {
            for (;;) {
                cip_buffer actual;
                int status = cip_receive(s, 30000, &actual); check(status);
                if (!status) return 3;
                if (actual.length == 0) { cip_buffer_free(actual); break; }
                if (actual.length < 8) return 3;
                uint64_t id = 0;
                for (unsigned j = 0; j < 8; j++) id |= (uint64_t)actual.data[j] << (8*j);
                if (id > UINT32_MAX) return 3;
                size_t length = message((unsigned)id, expected);
                if (actual.length != length || memcmp(actual.data, expected, length)) return 3;
                cip_buffer_free(actual);
                printf("%u\n", (unsigned)id); fflush(stdout);
            }
            cip_subscriber_close(s);
            return 0;
        }
        for (unsigned i = 0; i < count; i++) {
            size_t length = message(i, expected);
            cip_buffer actual;
            int status = cip_receive(s, 30000, &actual); check(status);
            if (!status || actual.length != length || memcmp(actual.data, expected, length)) return 3;
            cip_buffer_free(actual);
        }
        cip_subscriber_close(s);
    }
    return 0;
}
