using System.Globalization;
using LedgerAllocationLab.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace LedgerAllocationLab.Core.Tests;

public class BusinessDateClockTests
{
    protected readonly ServiceProvider _provider;

    public BusinessDateClockTests()
    {
        _provider = BuildProvider();
    }

    public static ServiceProvider BuildProvider()
    {
        // Arrange: Set up your configuration structure as dictionary pairs
        var inMemorySettings = new Dictionary<string, string?> {
            {"BusinessDateClock:IanaTimeZone", "America/Phoenix"}
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new ServiceCollection()
           .AddSingleton<IConfiguration>(configuration)
           .RegisterLedgerAllocationLabCoreServices()
           .BuildServiceProvider();
    }
    [Fact]
    public void Configuration_ReachesBusinessDateClockOptions()
    {
        var options = _provider.GetRequiredService<BusinessDateClockOptions>();

        Assert.False(string.IsNullOrWhiteSpace(options.IanaTimeZone));
    }

    // Phoenix is UTC-7 all year, so local midnight is 07:00 UTC.
    [Theory]
    [InlineData("2026-10-01T06:30:00", "2026-09-30")]     // 11:30 PM Sept 30 local
    [InlineData("2026-10-01T07:30:00", "2026-10-01")]     // 12:30 AM Oct 1 local
    [InlineData("2026-10-01T06:59:59.999", "2026-09-30")] // last millisecond of Sept 30
    [InlineData("2026-10-01T07:00:00", "2026-10-01")]     // local midnight exactly
    [InlineData("2028-03-01T06:30:00", "2028-02-29")]     // leap day, 11:30 PM local
    public void ToBusinessDate_UsesBusinessTimeZone(string utc, string expected)
    {
        var clock = _provider.GetRequiredService<BusinessDateClock>();
        var instant = DateTime.SpecifyKind(DateTime.Parse(utc, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), clock.ToBusinessDate(instant));
    }

    [Fact]
    public void DstAssumingOffset_MovesLateEveningPaymentToNextDay()
    {
        var clock = _provider.GetRequiredService<BusinessDateClock>();
        var instant = new DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc); // 11:30 PM Sept 30 in Phoenix
        var naive = DateOnly.FromDateTime(instant.AddHours(-6));            // the Report B mistake

        Assert.Equal(new DateOnly(2026, 9, 30), clock.ToBusinessDate(instant));
        Assert.Equal(new DateOnly(2026, 10, 1), naive);
    }

    [Fact]
    public void DstAssumingOffset_LeavesEarlyMorningPaymentAlone()
    {
        var clock = _provider.GetRequiredService<BusinessDateClock>();
        var instant = new DateTime(2026, 10, 1, 7, 30, 0, DateTimeKind.Utc); // 12:30 AM Oct 1 in Phoenix
        var naive = DateOnly.FromDateTime(instant.AddHours(-6));

        Assert.Equal(clock.ToBusinessDate(instant), naive);
    }

    [Fact]
    public void ToBusinessDate_RejectsLocalKind()
    {
        var clock = _provider.GetRequiredService<BusinessDateClock>();
        Assert.Throws<ArgumentException>(() => clock.ToBusinessDate(DateTime.Now));
    }
}
