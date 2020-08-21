using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal static class CancellationScope
    {
        private static readonly Action<object?> CancelAction = s => ((CancellationTokenSource)s!).Cancel();
        private static readonly ConcurrentStack<CancellationTokenSource> Stack;

        static CancellationScope()
        {
            var count = Math.Min(Environment.ProcessorCount * 16, 256);
            var sources = Enumerable.Range(0, count).Select(_ => new CancellationTokenSource());
            Stack = new ConcurrentStack<CancellationTokenSource>(sources);
        }

        internal static async ValueTask<TResult> ExecuteAsync<TState, TResult>(
            CancellationToken token1,
            CancellationToken token2,
            Func<TState, CancellationToken, ValueTask<TResult>> action,
            TState state)
        {
            if (Stack.TryPop(out var source))
            {
                try
                {
                    using (token1.Register(CancelAction, source, false))
                    using (token2.Register(CancelAction, source, false))
                        return await action(state, source.Token);
                }
                finally
                {
                    if (source.IsCancellationRequested)
                    {
                        source.Dispose();
                        source = new CancellationTokenSource();
                    }

                    Stack.Push(source);
                }
            }
            else
            {
                using (source = CancellationTokenSource.CreateLinkedTokenSource(token1, token2))
                    return await action(state, source.Token);
            }
        }
    }
}
