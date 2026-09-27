using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace LedgerAllocationLab.Data.Tests;

internal sealed class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(output, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class XunitLogger(ITestOutputHelper output, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                output.WriteLine($"[{logLevel}] {category}: {formatter(state, exception)}");
                if (exception is not null)
                {
                    output.WriteLine(exception.ToString());
                }
            }
            catch (InvalidOperationException)
            {
                // The test already finished; xUnit refuses late writes.
            }
        }
    }
}
