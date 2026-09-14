#ifndef CLOUDTOID_INTERPROCESS_H
#define CLOUDTOID_INTERPROCESS_H
#include <stddef.h>
#include <stdint.h>
#if defined(_WIN32) && !defined(CIP_STATIC)
#define CIP_API __declspec(dllimport)
#else
#define CIP_API
#endif
#ifdef __cplusplus
extern "C" {
#endif

typedef struct cip_publisher cip_publisher;
typedef struct cip_subscriber cip_subscriber;
typedef struct cip_buffer { uint8_t *data; size_t length; } cip_buffer;

typedef enum { CIP_ERROR = -1, CIP_UNAVAILABLE = 0, CIP_OK = 1 } cip_status;
typedef enum {
    CIP_NO_ERROR = 0, CIP_INVALID_ARGUMENT = 1, CIP_CAPACITY_MISMATCH = 2,
    CIP_PUBLISHER_LIMIT = 3, CIP_EXHAUSTED = 4, CIP_CORRUPT = 5,
    CIP_IO_ERROR = 6, CIP_INTERNAL_ERROR = 7
} cip_error_kind;

/* Status: 1 success/message, 0 full/empty/timeout, -1 error.
 * Strings are UTF-8 and NUL-terminated. path may be NULL for the temp directory.
 * Names/capacities must agree across participants. Windows ignores path.
 * cip_last_error is thread-local, valid until the next error on this thread.
 * Handles support concurrent calls. Close only after every call has returned;
 * no thread may use a closed handle. Close(NULL) is safe.
 * All pointers must remain valid for the call. Null buffers require size zero.
 * Wakeup failures do not turn committed sends/receives into errors.
 * Queues are transient: the last
 * endpoint closing or exiting loses unread messages. Reopening starts empty.
 */
CIP_API const char *cip_last_error(void);
CIP_API int32_t cip_last_error_kind(void);
CIP_API int32_t cip_publisher_open(const char *name, const char *path, size_t capacity, cip_publisher **output);
CIP_API int32_t cip_subscriber_open(const char *name, const char *path, size_t capacity, cip_subscriber **output);
CIP_API void cip_publisher_close(cip_publisher *handle);
CIP_API void cip_subscriber_close(cip_subscriber *handle);
CIP_API int32_t cip_try_send(const cip_publisher *handle, const uint8_t *data, size_t length);
/* timeout_ms: -1 waits indefinitely, 0 tries once, positive waits up to that limit.
 * Use bounded timeouts when the caller needs cancellation or orderly shutdown.
 * Free each successful result exactly once with cip_buffer_free, including empty
 * messages. Copying a cip_buffer does not transfer or duplicate its ownership. */
CIP_API int32_t cip_receive(const cip_subscriber *handle, int64_t timeout_ms, cip_buffer *output);
CIP_API void cip_buffer_free(cip_buffer buffer);
/* An undersized buffer truncates AND consumes the message, matching .NET v3. */
CIP_API int32_t cip_try_receive_into(const cip_subscriber *handle, uint8_t *data, size_t capacity, size_t *copied);
#ifdef __cplusplus
}
#endif
#endif
