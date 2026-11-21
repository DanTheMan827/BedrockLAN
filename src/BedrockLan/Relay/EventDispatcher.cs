// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Channels;

namespace BedrockLan;

/// <summary>
/// Runs event handlers one at a time, in the order they were raised, on a dedicated background task.
/// Handlers therefore never run under an internal lock or on a socket thread, may call back into the relay
/// (even dispose it), and a slow or throwing handler cannot break networking.
/// </summary>
internal sealed class EventDispatcher
{
    private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<LanLogger?> _log;
    private readonly Task _pump;

    public EventDispatcher(Func<LanLogger?> log)
    {
        _log = log;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Queues a handler invocation; silently dropped once the dispatcher is completed.</summary>
    public void Post(Action action) => _queue.Writer.TryWrite(action);

    /// <summary>True when called from inside an event handler.</summary>
    public static bool IsDispatching => t_dispatching;

    [ThreadStatic]
    private static bool t_dispatching;

    private async Task PumpAsync()
    {
        await foreach (var action in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            t_dispatching = true;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log().Error($"An event handler threw: {ex}");
            }
            finally
            {
                t_dispatching = false;
            }
        }
    }

    /// <summary>Stops accepting events and waits (briefly) for queued ones to run.</summary>
    public async Task CompleteAsync()
    {
        _queue.Writer.TryComplete();
        if (IsDispatching)
        {
            return; // disposing from inside a handler: can't wait for ourselves
        }

        await Task.WhenAny(_pump, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
    }
}
