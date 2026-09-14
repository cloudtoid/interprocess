namespace Cloudtoid.Interprocess.Tests;

public sealed class PublisherRecoveryTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    [Fact]
    public void RecoverySeesAllCallsUntilTheLastCallLeaves()
    {
        var options = Options();
        using var keeper = new Probe(options);
        using var lease = keeper.Lease(keeper.Register());
        keeper.AnyActive().Should().BeFalse();
        lease.Enter();
        lease.Enter();
        try
        {
            keeper.AnyActive().Should().BeTrue();
            lease.Exit();
            keeper.AnyActive().Should().BeTrue();
        }
        finally
        {
            lease.Exit();
        }

        keeper.AnyActive().Should().BeFalse();
    }

    [Fact]
    public void MissingRegistrationIsInspectedAgainAfterItAppears()
    {
        var options = Options();
        using var keeper = new Probe(options);
        var id = keeper.Register();
        keeper.AnyActive().Should().BeFalse();
        using var lease = keeper.Lease(id);
        lease.Enter();
        try
        {
            keeper.AnyActive().Should().BeTrue();
        }
        finally
        {
            lease.Exit();
        }
    }

    [Fact]
    public void RecoveryGateRejectsExistingAndNewPublishersUntilReopened()
    {
        var options = Options();
        var factory = new QueueFactory();
        using var keeper = new Probe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        keeper.CloseAdmission();
        using var newcomer = factory.CreatePublisher(options);
        try
        {
            publisher.TryEnqueue("existing"u8).Should().BeFalse();
            newcomer.TryEnqueue("newcomer"u8).Should().BeFalse();
        }
        finally
        {
            keeper.OpenAdmission();
        }

        publisher.TryEnqueue("existing"u8).Should().BeTrue();
        newcomer.TryEnqueue("newcomer"u8).Should().BeTrue();
        subscriber.TryDequeue(out var first).Should().BeTrue();
        first.ToArray().Should().Equal("existing"u8.ToArray());
        subscriber.TryDequeue(out var second).Should().BeTrue();
        second.ToArray().Should().Equal("newcomer"u8.ToArray());
    }

    [Fact]
    public void DisposedRegistrationsReuseSlotsAcrossQueueHistory()
    {
        using var keeper = new Probe(Options());
        for (var i = 0; i < PublisherRegistry.MaximumPublishers * 2; i++)
        {
            using var lease = keeper.Lease(keeper.Register());
            lease.Enter();
            keeper.AnyActive().Should().BeTrue();
            lease.Exit();
        }

        keeper.AnyActive().Should().BeFalse();
    }

    [Fact]
    public void ConcurrentRegistrationsReuseSlots()
    {
        using var keeper = new Probe(Options());
        for (var pass = 0; pass < 2; pass++)
        {
            var leases = Enumerable
                .Range(0, 80)
                .AsParallel()
                .Select(_ => keeper.Lease(keeper.Register()))
                .ToArray();
            try
            {
                foreach (var lease in leases)
                    lease.Enter();

                foreach (var lease in leases)
                {
                    keeper.AnyActive().Should().BeTrue();
                    lease.Exit();
                }

                keeper.AnyActive().Should().BeFalse();
            }
            finally
            {
                foreach (var lease in leases)
                    lease.Dispose();
            }
        }
    }

    [Fact]
    public void FullTableRejectsAnotherPublisherAndReusesReleasedSlot()
    {
        var options = Options();
        var factory = new QueueFactory();
        using var subscriber = factory.CreateSubscriber(options);
        var publishers = new List<IPublisher>();
        try
        {
            for (var i = 0; i < PublisherRegistry.MaximumPublishers; i++)
                publishers.Add(factory.CreatePublisher(options));

            var create = () => factory.CreatePublisher(options);
            create.Should().Throw<InvalidOperationException>().WithMessage("*2048*");

            // The final slot must be independent of both the table boundary and the ring.
            for (var i = 0; i < 20; i++)
            {
                publishers[^1].TryEnqueue("last-slot"u8).Should().BeTrue();
                subscriber.TryDequeue(out var message).Should().BeTrue();
                message.ToArray().Should().Equal("last-slot"u8.ToArray());
            }

            publishers[0].Dispose();
            using var replacement = factory.CreatePublisher(options);
            replacement.TryEnqueue("reused"u8).Should().BeTrue();
            subscriber.TryDequeue(out var reused).Should().BeTrue();
            reused.ToArray().Should().Equal("reused"u8.ToArray());
            create.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            foreach (var publisher in publishers)
                publisher.Dispose();
        }
    }

    [Fact]
    public void FullTableReclaimsDeadRegistrationWithUnfinishedCall()
    {
        var options = Options();
        using var keeper = new Probe(options);
        var leases = new List<PublisherLease>();
        try
        {
            // Leave only the final slot available, containing a dead owner's count.
            for (var i = 0; i < PublisherRegistry.MaximumPublishers - 1; i++)
                leases.Add(keeper.Lease(keeper.Register()));

            keeper.LeaveDeadRegistrationInLastSlot(options);
            keeper.AnyActive().Should().BeFalse();
            using var replacement = keeper.Lease(keeper.Register());
            keeper.AnyActive().Should().BeFalse("the dead owner's count must be reset");
            replacement.Enter();
            keeper.AnyActive().Should().BeTrue();
            replacement.Exit();
            keeper.AnyActive().Should().BeFalse();
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    private QueueOptions Options() => new(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);

    private sealed class Probe(QueueOptions options) : Queue(options, NullLoggerFactory.Instance)
    {
        internal unsafe void LeaveDeadRegistrationInLastSlot(QueueOptions registrationOptions)
        {
            var id = Register();
            using var lifetime = new ReaderLease(registrationOptions, id);
            var slot = (byte*)Header + PublisherRegistry.BufferOffset - PublisherRegistry.SlotSize;
            *(long*)slot = id;
            *(int*)(slot + sizeof(long)) = 1;
        }

        internal long Register() => RegisterParticipant();
        internal PublisherLease Lease(long id) => Publishers.Register(id);
        internal bool AnyActive() => Publishers.AnyActive();
        internal unsafe void CloseAdmission() => Interlocked.Exchange(ref Header->ReadLockOwner, long.MinValue | 1);
        internal unsafe void OpenAdmission() => Interlocked.Exchange(ref Header->ReadLockOwner, 0);
    }
}