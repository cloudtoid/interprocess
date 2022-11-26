using static Cloudtoid.Contract;
using SysPath = System.IO.Path;

namespace Cloudtoid.Interprocess
{
    public sealed class QueueOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueueOptions"/> class.
        /// </summary>
        /// <param name="queueName">The unique name of the queue.</param>
        /// <param name="capacity">The maximum capacity of the queue in bytes. This should be at least 16 bytes long and in the multiples of 8</param>
        public QueueOptions(string queueName, long capacity)
            : this(queueName, SysPath.GetTempPath(), capacity)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueOptions"/> class.
        /// </summary>
        /// <param name="queueName">The unique name of the queue.</param>
        /// <param name="path">The path to the directory/folder in which the memory mapped and other files are stored in</param>
        /// <param name="capacity">The maximum capacity of the queue in bytes. This should be at least 16 bytes long and in the multiples of 8</param>
        public unsafe QueueOptions(string queueName, string path, long capacity)
        {
            QueueName = CheckNonEmpty(queueName, nameof(queueName));
            Path = CheckValue(path, nameof(path));

            Capacity = CheckGreaterThan(capacity, 16, nameof(capacity));
            CheckParam((capacity % 8) == 0, nameof(queueName), "messageCapacityInBytes should be a multiple of 8 (8 bytes = 64 bits).");
        }

        /// <summary>
        /// Gets the unique name of the queue.
        /// </summary>
        public string QueueName { get; }

        /// <summary>
        /// Gets the path to the directory/folder in which the memory mapped and other files are stored in.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the size of the queue in bytes. This does NOT include the space needed for the queue header.
        /// </summary>
        public long Capacity { get; }

        /// <summary>
        /// Gets the full size of the queue that includes both the header and message sections
        /// </summary>
        internal unsafe long GetQueueStorageSize()
            => sizeof(QueueHeader) + Capacity;
    }
}
