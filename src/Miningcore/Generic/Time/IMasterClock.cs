using System;

namespace Miningcore.Time
{
    public interface IMasterClock
    {
        DateTime Now { get; }
        DateTime UtcNow { get; }
    }
}
