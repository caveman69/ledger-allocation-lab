using System.Data;
using Dapper;
using LedgerAllocationLab.Core;
using LedgerAllocationLab.Data.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerAllocationLab.Data.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection RegisterLedgerAllocationLabDataServices(this IServiceCollection services)
    {
        SqlMapper.AddTypeHandler(new SqlDateOnlyTypeHandler());
        SqlMapper.AddTypeMap(typeof(DateTime), DbType.DateTime2);
        SqlMapper.AddTypeMap(typeof(DateTime?), DbType.DateTime2);

        services.AddOptions<LedgerLabDbOptions>()
        .BindConfiguration(LedgerLabDbOptions.SectionName)
        .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "LedgerLabDb:ConnectionString is not set.")
        .ValidateOnStart();

        // Implementation for registering data services
        return services.AddSingleton<LedgerLabDapperDbContext>()
            .AddScoped<IPaymentsDbService, PaymentsDbService>()
            .AddScoped<IReportsDbService, ReportsDbService>();
    }
}
