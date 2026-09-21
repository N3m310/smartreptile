using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Infrastructure.Health;
using SmartReptile.Infrastructure.Mqtt;
using SmartReptile.Infrastructure.Observability;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Infrastructure.Time;

namespace SmartReptile.Infrastructure;

/// <summary>Composition root for everything the API needs from infrastructure.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers options (validated at start-up), the database context, the in-process MQTT broker and the
    /// observability singletons.
    /// </summary>
    public static IServiceCollection AddSmartReptileInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddOptions<MqttBrokerOptions>()
            .Bind(configuration.GetSection(MqttBrokerOptions.SectionName))
            .Validate(
                o => !o.EnableTls || !string.IsNullOrWhiteSpace(o.ServerCertificatePath),
                "Mqtt:ServerCertificatePath is required when Mqtt:EnableTls is true")
            .Validate(
                o => !o.DisablePlaintextEndpoint || (o.EnableTls && !string.IsNullOrWhiteSpace(o.ServerCertificatePath)),
                "Mqtt:DisablePlaintextEndpoint requires a TLS listener with a certificate")
            .Validate(o => o.Port is > 0 and < 65536, "Mqtt:Port must be a valid port")
            .ValidateOnStart();

        services.AddOptions<RetentionOptions>().Bind(configuration.GetSection(RetentionOptions.SectionName)).ValidateOnStart();
        services.AddOptions<DefaultsOptions>().Bind(configuration.GetSection(DefaultsOptions.SectionName)).ValidateOnStart();
        services.AddOptions<StartupOptions>().Bind(configuration.GetSection(StartupOptions.SectionName));
        services.AddOptions<IngestOptions>().Bind(configuration.GetSection(IngestOptions.SectionName)).ValidateOnStart();
        services.AddOptions<ProvisioningOptions>()
            .Bind(configuration.GetSection(ProvisioningOptions.SectionName))
            .Validate(o => o.ClaimCodeMinutes is > 0 and <= 60, "Provisioning:ClaimCodeMinutes must be between 1 and 60")
            .ValidateOnStart();
        services.AddOptions<OnboardingProtectionOptions>()
            .Bind(configuration.GetSection(OnboardingProtectionOptions.SectionName))
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required (see .env.example).");

        services.AddDbContext<SmartReptileDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.MigrationsAssembly(typeof(SmartReptileDbContext).Assembly.FullName);
            }));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<SmartReptileMetrics>();
        services.AddSingleton<MqttBrokerStatus>();
        services.AddSingleton<IMqttBrokerStatus>(sp => sp.GetRequiredService<MqttBrokerStatus>());
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MqttBrokerOptions>>().Value);
        services.AddHostedService<MqttBrokerHostedService>();

        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<DatabaseInitializer>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<MqttBrokerHealthCheck>("mqtt-broker", tags: ["ready"]);

        return services;
    }
}
