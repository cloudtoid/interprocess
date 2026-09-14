#ifndef CLOUDTOID_INTERPROCESS_H
#define CLOUDTOID_INTERPROCESS_H
#include <stddef.h>
#include <stdint.h>
#ifdef _WIN32
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

/* Status: 1 success/message, 0 full/empty/timeout, -1 error.
 * Strings are UTF-8 and NUL-terminated. path may be NULL for the temp directory.
 * Names/capacities must agree across participants. Windows ignores path.
 * cip_last_error is thread-local, valid until the next error on this thread.
 * Handles support concurrent calls. Close only after every call has returned;
 * no thread may use a closed handle. Close(NULL) is safe.
 * All pointers must remain valid for the call. Null buffers require size zero.
 * An error after reservation can leave a committed message: do not retry errors
 * blindly when duplicate delivery matters. Queues are transient: the last
 * endpoint closing or exiting loses unread messages. Reopening starts empty.
 */
CIP_API const char *cip_last_error(void);
CIP_API int32_t cip_publisher_open(const char *name, const char *path, size_t capacity, cip_publisher **output);
CIP_API int32_t cip_subscriber_open(const char *name, const char *path, size_t capacity, cip_subscriber **output);
CIP_API void cip_publisher_close(cip_publisher *handle);
CIP_API void cip_subscriber_close(cip_subscriber *handle);
CIP_API int32_t cip_try_send(const cip_publisher *handle, const uint8_t *data, size_t length);
/* timeout_ms: -1 waits indefinitely, 0 tries once, positive waits up to that limit.
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
