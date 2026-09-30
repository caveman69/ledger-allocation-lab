using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerAllocationLab.Core.Extensions;

public static class RegisterLedgerAllocationsLabCoreExtensions
{
    public static IServiceCollection RegisterLedgerAllocationLabCoreServices(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        var options = new BusinessDateClockOptions();
        configuration.GetSection(BusinessDateClockOptions.SectionName).Bind(options);

        // Manually trigger Data Annotation validation for the raw object
        var validationContext = new ValidationContext(options);
        Validator.ValidateObject(options, validationContext, validateAllProperties: true);

        // This will only execute if validation passes
        services.AddSingleton(options)
            .AddSingleton<BusinessDateClock>();

        return services;
    }
}
