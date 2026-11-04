using System.Diagnostics;

namespace cifraclub_scraper_backend_csharp;

public class AdaptiveRateGovernor
{
    private readonly object _syncRoot = new();
    private double _currentDelayMs;
    private readonly double _minDelayMs;
    private readonly double _maxDelayMs;
    private double _bestStableDelayMs;
    private int _consecutiveSuccesses;
    private int _rateLimitHits;
    private int _totalRequests;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private DateTime _nextDispatchUtc = DateTime.UtcNow;

    public AdaptiveRateGovernor(double initialDelayMs = 200, double minDelayMs = 80, double maxDelayMs = 3000)
    {
        _currentDelayMs = initialDelayMs;
        _minDelayMs = minDelayMs;
        _maxDelayMs = maxDelayMs;
        _bestStableDelayMs = initialDelayMs;
    }

    public async Task WaitTurnAsync(CancellationToken ct = default)
    {
        TimeSpan waitDuration;

        lock (_syncRoot)
        {
            var now = DateTime.UtcNow;

            // If in cooldown from an Akamai 429, schedule after cooldown expires
            var baseline = now > _cooldownUntil ? now : _cooldownUntil;
            if (_nextDispatchUtc < baseline)
            {
                _nextDispatchUtc = baseline;
            }

            var dispatchTime = _nextDispatchUtc;
            _nextDispatchUtc = dispatchTime.AddMilliseconds(_currentDelayMs);
            _totalRequests++;

            waitDuration = dispatchTime > now ? dispatchTime - now : TimeSpan.Zero;
        }

        if (waitDuration > TimeSpan.Zero)
        {
            await Task.Delay(waitDuration, ct);
        }
    }

    public void ReportSuccess()
    {
        lock (_syncRoot)
        {
            _consecutiveSuccesses++;

            // Every 4 consecutive successes without hitting rate limit, accelerate (reduce delay)
            if (_consecutiveSuccesses >= 4)
            {
                _consecutiveSuccesses = 0;
                
                // If we have previously hit a rate limit, accelerate more conservatively
                var reduction = _rateLimitHits > 0
                    ? Math.Max(5.0, _currentDelayMs * 0.05)
                    : Math.Max(10.0, _currentDelayMs * 0.10);

                _currentDelayMs = Math.Max(_minDelayMs, _currentDelayMs - reduction);

                if (_currentDelayMs < _bestStableDelayMs)
                {
                    _bestStableDelayMs = _currentDelayMs;
                }
            }
        }
    }

    public void ReportRateLimit(double cooldownSeconds = 5.0)
    {
        lock (_syncRoot)
        {
            var now = DateTime.UtcNow;
            _rateLimitHits++;
            _consecutiveSuccesses = 0;

            // Avoid compounded multiplicative decrease if multiple in-flight requests hit 429 in the same window
            if (now >= _cooldownUntil)
            {
                // Multiplicative increase of delay (decrease of throughput)
                _currentDelayMs = Math.Min(_maxDelayMs, Math.Max(_currentDelayMs * 1.6, 250));
            }

            _cooldownUntil = now.AddSeconds(cooldownSeconds);
            _nextDispatchUtc = _cooldownUntil;
        }
    }

    public RateMetrics GetMetrics(TimeSpan totalElapsed)
    {
        lock (_syncRoot)
        {
            // Sweet spot in seconds:
            // If we never hit a rate limit, it's the fastest stable delay reached.
            // If rate limits were hit, the sweet spot is the calibrated stable delay (backed off safely).
            var sweetSpotMs = _rateLimitHits == 0
                ? _bestStableDelayMs
                : Math.Max(_bestStableDelayMs, _currentDelayMs);

            var sweetSpotSec = Math.Round(sweetSpotMs / 1000.0, 3);
            var rps = sweetSpotSec > 0 ? Math.Round(1.0 / sweetSpotSec, 2) : 0;

            return new RateMetrics(
                SweetSpotSeconds: sweetSpotSec,
                SweetSpotMs: Math.Round(sweetSpotMs, 1),
                RequestsPerSecond: rps,
                RateLimitHits: _rateLimitHits,
                TotalRequests: _totalRequests,
                TotalDurationSeconds: Math.Round(totalElapsed.TotalSeconds, 2)
            );
        }
    }
}
