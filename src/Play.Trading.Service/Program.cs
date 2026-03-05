using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
//using MassTransit.MongoDbIntegration.Saga;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
// MongoDB types are not required directly here; repositories are registered via Play.Common extensions
using Play.Common.Identity;
using Play.Common.Repositories;
using Play.Common.Settings;
using Play.Identity.Contracts;
using Play.Inventory.Contracts;
using Play.Trading.Service;
using Play.Trading.Service.Clients;
using Play.Trading.Service.Contracts;
using Play.Trading.Service.Entities;
using Play.Trading.Service.Exceptions;
using Play.Trading.Service.Persistence;
using Play.Trading.Service.Services;
using Play.Trading.Service.Settings;
using Play.Trading.Service.SignalR;
using Play.Trading.Service.StatesMachine;
using Polly;
using Polly.Timeout;
using Serilog;
using System.Reflection;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.Standard));
BsonSerializer.RegisterSerializer(typeof(Guid?), new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));

const string AllowedOriginSetting = "AllowedOrigin";

Log.Logger = new LoggerConfiguration()
             .WriteTo.Console()
             //.WriteTo.File("logs/invent_log.txt")
             .MinimumLevel.Information()
             .CreateLogger();

// Add services to the container.

builder.Services.Configure<CosmosDbSettings>(
    builder.Configuration.GetSection(nameof(CosmosDbSettings)));

builder.Services.Configure<ServiceBusSettings>(
    builder.Configuration.GetSection(nameof(ServiceBusSettings)));

builder.Services.Configure<ServiceSettings>(
    builder.Configuration.GetSection(nameof(ServiceSettings)));

builder.Services.Configure<MassTransitSettings>(
    builder.Configuration.GetSection(nameof(MassTransitSettings)));

builder.Services.Configure<QueueSettings>(
    builder.Configuration.GetSection(nameof(QueueSettings)));

builder.Services.Configure<SqlDbSettings>(
    builder.Configuration.GetSection(nameof(SqlDbSettings)));

// Register MongoDB infrastructure and repositories from Play.Common
builder.Services.AddMongoDb()
                .AddMongoRepository<ApplicationUser>("users", databaseName: "Identity");

//datasync with other microservices to keep data consistent

builder.Services.AddSingleton<TradingCatalogSyncService>();
builder.Services.AddSingleton<TradingInventorySyncService>();

builder.Services.AddCosmosDb()
                .AddCosmosRepository<CatalogItem>("catalogitems")
                .AddCosmosRepository<InventoryItem>("inventoryitems")
                .AddCosmosRepository<ApplicationUser>("users")
                .AddJwtBearerAuthentication();

builder.Services.AddDbContext<PurchaseStateDbContext>(options =>
{
    var sql = builder.Configuration.GetSection(nameof(SqlDbSettings)).Get<SqlDbSettings>();
    options.UseSqlServer(sql.ConnectionString);

});

AddMassTransit();

builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>()
                .AddSingleton<MessageHub>()
                .AddSignalR();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.Read, policy =>
    {
        policy.RequireRole("Admin");
        policy.RequireClaim("scope", "catalog.fullaccess", "inventory.fullaccess");
    });

    options.AddPolicy(Policies.Write, policy =>
    {
        policy.RequireRole("Admin");
        policy.RequireClaim("scope", "catalog.fullaccess", "inventory.fullaccess");
    });

});
builder.Services.AddControllers(option =>
{
    option.SuppressAsyncSuffixInActionNames = false;
}).AddJsonOptions(options =>
   {
       options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
   });

//builder.Services.AddOpenApi();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Play.Trading.Service", Version = "v1" });
});

// register token provider and handler for outgoing client calls
builder.Services.AddSingleton<ITokenProvider, ClientCredentialsTokenProvider>();
builder.Services.AddTransient<TokenDelegatingHandler>();

var clientServicesSettings = builder.Configuration
    .GetSection(nameof(ClientServicesSettings))
    .Get<ClientServicesSettings>();

AddCatalogClient(builder.Services, clientServicesSettings);
AddInventoryClient(builder.Services, clientServicesSettings);

builder.Services.AddHealthChecks();

var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.MapOpenApi();
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Play.Trading.Service v1"));

    app.UseCors(config => {
        config.WithOrigins(builder.Configuration[AllowedOriginSetting])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
}
else
{
    app.UseHttpsRedirection();
}


app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapHub<MessageHub>("/messagehub");
});


void AddMassTransit()
{
    builder.Services.AddMassTransit(configure =>
    {
        configure.AddConsumers(Assembly.GetEntryAssembly());

        configure.AddSagaStateMachine<PurchaseStateMachine, PurchaseState>()
           .EntityFrameworkRepository(r =>
           {
               r.ExistingDbContext<PurchaseStateDbContext>();
               r.LockStatementProvider = new SqlServerLockStatementProvider();

           });

        configure.UsingAzureServiceBus((context, cfg) =>
        {
            var sb = builder.Configuration.GetSection(nameof(ServiceBusSettings)).Get<ServiceBusSettings>();

            cfg.Host(sb.FullyQualifiedNamespace);

            cfg.UseInMemoryOutbox(context);

            cfg.UseMessageRetry(r =>
            {
                r.Interval(3, TimeSpan.FromSeconds(5));
                r.Ignore(typeof(UnknownItemException));
            });

            cfg.ConfigureEndpoints(context);
        });
    });
}

static void AddCatalogClient(IServiceCollection serviceCollection, ClientServicesSettings clientServicesSettings)
{
    var catalog = clientServicesSettings.ClientServices
    .FirstOrDefault(s => s.ServiceName.Equals("CatalogService", StringComparison.OrdinalIgnoreCase));

    Random jitterer = new Random();

    serviceCollection.AddHttpClient<CatalogClient>(client =>
    {
        client.BaseAddress = new Uri(catalog?.ServiceUrl);
    })
    .AddHttpMessageHandler<TokenDelegatingHandler>()
    .AddTransientHttpErrorPolicy(policy => policy.Or<TimeoutRejectedException>().WaitAndRetryAsync(
        5, // 5 attempts
        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(jitterer.Next(0, 1000)), // exponentinal backoff
        onRetry: (outcome, timespan, retryAttemp) =>
        {
            // Use Serilog static logger instead of building a service provider here
            Log.Logger.ForContext("SourceContext", typeof(CatalogClient).FullName)
                .Warning("Delaying for {Delay} seconds, then making retry {Retry}", timespan, retryAttemp);
        }))
    .AddTransientHttpErrorPolicy(policy => policy.Or<TimeoutRejectedException>().CircuitBreakerAsync(
              3,
              TimeSpan.FromSeconds(15),
              onBreak: (outcome, timespan) =>
              {
                  Log.Logger.ForContext("SourceContext", typeof(CatalogClient).FullName)
                      .Warning("Opening the Circuit for {Seconds} seconds...", timespan.TotalSeconds);
              },
              onReset: () =>
              {
                  Log.Logger.ForContext("SourceContext", typeof(CatalogClient).FullName)
                      .Warning("Closing the Circuit...");
              }
          ))
    .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(1));
}

static void AddInventoryClient(IServiceCollection serviceCollection, ClientServicesSettings clientServicesSettings)
{
    var inventory = clientServicesSettings.ClientServices
    .FirstOrDefault(s => s.ServiceName.Equals("InventoryService", StringComparison.OrdinalIgnoreCase));

    Random jitterer = new Random();

    serviceCollection.AddHttpClient<InventoryClient>(client =>
    {
        client.BaseAddress = new Uri(inventory?.ServiceUrl);
    })
    .AddHttpMessageHandler<TokenDelegatingHandler>()
    .AddTransientHttpErrorPolicy(policy => policy.Or<TimeoutRejectedException>().WaitAndRetryAsync(
        5, // 5 attempts
        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(jitterer.Next(0, 1000)), // exponentinal backoff
        onRetry: (outcome, timespan, retryAttemp) =>
        {
            // Use Serilog static logger instead of building a service provider here
            Log.Logger.ForContext("SourceContext", typeof(InventoryClient).FullName)
                .Warning("Delaying for {Delay} seconds, then making retry {Retry}", timespan, retryAttemp);
        }))
    .AddTransientHttpErrorPolicy(policy => policy.Or<TimeoutRejectedException>().CircuitBreakerAsync(
              3,
              TimeSpan.FromSeconds(15),
              onBreak: (outcome, timespan) =>
              {
                  Log.Logger.ForContext("SourceContext", typeof(InventoryClient).FullName)
                      .Warning("Opening the Circuit for {Seconds} seconds...", timespan.TotalSeconds);
              },
              onReset: () =>
              {
                  Log.Logger.ForContext("SourceContext", typeof(InventoryClient).FullName)
                      .Warning("Closing the Circuit...");
              }
          ))
    .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(1));
}

app.MapHealthChecks("/health");

app.Run();

