using static Cloudtoid.Contract;

namespace Cloudtoid.Interprocess
{
    public sealed class QueueOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueueOptions"/> class.
        /// </summary>
        /// <param name="queueName">The unique name of the queue.</param>
        /// <param name="bytesCapacity">The maximum capacity of the queue in bytes. This should be at least 16 bytes long and in the multiples of 8</param>
        /// <param name="createOrOverride">Specifies whether the backing shared memory storage for a queue with the same <paramref name="queueName"/> should be overwritten.</param>
        public QueueOptions(
            string queueName,
            long bytesCapacity,
            bool createOrOverride = false)
            : this(
                  queueName,
                  System.IO.Path.GetTempPath(),
                  bytesCapacity,
                  createOrOverride)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueOptions"/> class.
        /// </summary>
        /// <param name="queueName">The unique name of the queue.</param>
        /// <param name="path">The path to the directory/folder in which the memory mapped and other files are stored in</param>
        /// <param name="bytesCapacity">The maximum capacity of the queue in bytes. This should be at least 16 bytes long and in the multiples of 8</param>
        /// <param name="createOrOverride">Specifies whether the backing shared memory storage for a queue
        /// with the same <paramref name="queueName"/> in the same <paramref name="path"/> should be overwritten.</param>
        public unsafe QueueOptions(
            string queueName,
            string path,
            long bytesCapacity,
            bool createOrOverride = false)
        {
            QueueName = CheckNonEmpty(queueName, nameof(queueName));
            Path = CheckValue(path, nameof(path));

            BytesCapacity = CheckGreaterThan(bytesCapacity, sizeof(QueueHeader), nameof(bytesCapacity));
            CheckParam((bytesCapacity % 8) == 0, nameof(queueName), $"{nameof(bytesCapacity)} should be a multiple of 8 (8 bytes = 64 bits).");

            CreateOrOverride = createOrOverride;
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
        /// Gets the maximum capacity of the queue in bytes.
        /// </summary>
        public long BytesCapacity { get; }

        /// <summary>
        /// Gets whether the backing shared memory storage for a queue with the same name in the same location should be overwritten.
        /// </summary>
        public bool CreateOrOverride { get; }
    }
}
