using System;

namespace Cloudtoid.Interprocess
{
    public sealed class QueueOptions
    {
        /// <summary>
        /// Creates an instance of <see cref="QueueOptions"/>
        /// </summary>
        /// <param name="queueName">The unique name of the queue.</param>
        /// <param name="path">The path to the directory/folder in which the memory mapped and other files are stored in</param>
        /// <param name="capacity">The maximum capacity of the queue in bytes</param>
        /// <param name="createOrOverride">Specifies whether the backing shared memory storage for a queue
        /// with the same <paramref name="queueName"/> in the same <paramref name="path"/> should be overwritten.</param>
        public QueueOptions(
            string queueName,
            string path,
            long capacity,
            bool createOrOverride)
        {
            QueueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
            Path = path ?? throw new ArgumentNullException(nameof(path));

            if (queueName.Length == 0)
                throw new ArgumentException($"{nameof(queueName)} cannot be an empty string", nameof(queueName));

            if (capacity < 16 && (capacity % 8) == 0)
                throw new ArgumentException($"{nameof(capacity)} should be at least 16 bytes long and in the multiples of 8 (8 bytes = 64 bits).");

            Capacity = capacity;
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
        public long Capacity { get; }

        /// <summary>
        /// Gets whether the backing shared memory storage for a queue with the same name in the same location should be overwritten.
        /// </summary>
        public bool CreateOrOverride { get; }
    }
}
