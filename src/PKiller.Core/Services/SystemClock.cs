using PKiller.Core.Abstractions;

namespace PKiller.Core.Services;

public sealed class SystemClock : IClock
{
    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
