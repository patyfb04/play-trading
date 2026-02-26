using MassTransit;
//using MassTransit.MongoDbIntegration.Saga;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
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
using Play.Trading.Service.Repository;
using Play.Trading.Service.Services;
using Play.Trading.Service.Settings;
using Play.Trading.Service.SignalR;
using Play.Trading.Service.StatesMachine;
using Serilog;
using System.Reflection;
using System.Text.Json.Serialization;
using Polly;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

BsonDefaults.GuidRepresentationMode = GuidRepresentationMode.V3;
BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.Standard));
BsonSerializer.RegisterSerializer(typeof(Guid?), new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));

const string AllowedOriginSetting = "AllowedOrigin";

Log.Logger = new LoggerConfiguration()
             .WriteTo.Console()
             //.WriteTo.File("logs/invent_log.txt")
             .MinimumLevel.Information()
             .CreateLogger();

// Add services to the container.

builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection(nameof(MongoDbSettings)));

builder.Services.Configure<ServiceSettings>(
    builder.Configuration.GetSection(nameof(ServiceSettings)));

builder.Services.Configure<MassTransitSettings>(
    builder.Configuration.GetSection(nameof(MassTransitSettings)));

builder.Services.Configure<QueueSettings>(
    builder.Configuration.GetSection(nameof(QueueSettings)));

builder.Services.AddSingleton<ITradingUserRepository>(sp =>
{
    var client = sp.GetRequiredService<MongoClient>();
    var database = client.GetDatabase("Identity");
    return new TradingUserRepository(database, "Users");
});

//datasync with other microservices to keep data consistent

builder.Services.AddSingleton<TradingCatalogSyncService>();
builder.Services.AddSingleton<TradingInventorySyncService>();

builder.Services.AddMongoDb()
                .AddMongoRepository<CatalogItem>("catalogitems")
                .AddMongoRepository<InventoryItem>("inventoryitems")
                .AddMongoRepository<ApplicationUser>("users")
                .AddJwtBearerAuthentication();

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

// Sync the services items with trading items on startup
// using (var scope = app.Services.CreateScope())
// {
//     var syncCatalog = scope.ServiceProvider.GetRequiredService<TradingCatalogSyncService>();
//     await syncCatalog.RunAsync();
// }

// using (var scope = app.Services.CreateScope())
// {
//     var syncInventory = scope.ServiceProvider.GetRequiredService<TradingInventorySyncService>();
//     await syncInventory.RunAsync();
// }

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
        configure.AddConsumers(Assembly.GetEntryAssembly()); // all consumers found in the entry assembly are register with mass transit

        configure.AddSagaStateMachine<PurchaseStateMachine, PurchaseState>()
        .InMemoryRepository();
        //.MongoDbRepository(repo => {
        //    var serviceSettings = builder.Configuration.GetSection(nameof(ServiceSettings)).Get<ServiceSettings>();
        //    var mongoSettings = builder.Configuration.GetSection(nameof(MongoDbSettings)).Get<MongoDbSettings>();

        //    repo.Connection = mongoSettings.ConnectionString;
        //    repo.DatabaseName = serviceSettings.ServiceName;
        //    repo.CollectionName = "purchaseStates";

        //});

        var queueSettings = builder.Configuration.GetSection(nameof(QueueSettings)).Get<QueueSettings>();

        configure.UsingRabbitMq((context, cfg) =>
        {
            var rabbitSettings = builder.Configuration.GetSection(nameof(RabbitMQSettings)).Get<RabbitMQSettings>();
            
            cfg.UseInMemoryOutbox(context);

            cfg.UseMessageRetry(config =>
            {
                config.Interval(3, TimeSpan.FromSeconds(5));
                config.Ignore(typeof(UnknownItemException));
            });

            cfg.Host(rabbitSettings.Host, h =>
            {
                h.Username(rabbitSettings.Username);
                h.Password(rabbitSettings.Password);
            });


            cfg.ReceiveEndpoint("purchase-requested-faults", e =>
            {
                e.Handler<Fault<PurchaseRequested>>(context =>
                {
                    var exception = context.Message.Exceptions.FirstOrDefault();
                    Console.WriteLine($"Fault captured: {exception?.Message}");
                    return Task.CompletedTask;
                });

                e.Handler<Fault<GilDebited>>(context =>
                {
                    var exception = context.Message.Exceptions.FirstOrDefault();
                    Console.WriteLine($"Fault captured: {exception?.Message}");
                    return Task.CompletedTask;
                });

                e.Handler<Fault<InventoryItemsGranted>>(context =>
                {
                    var exception = context.Message.Exceptions.FirstOrDefault();
                    Console.WriteLine($"Fault captured: {exception?.Message}");
                    return Task.CompletedTask;
                });

                e.Handler<Fault<InventoryItemsSubtracted>>(context =>
                {
                    var exception = context.Message.Exceptions.FirstOrDefault();
                    Console.WriteLine($"Fault captured: {exception?.Message}");
                    return Task.CompletedTask;
                });

            });

            cfg.ConfigureEndpoints(context);
        });

        EndpointConvention.Map<GrantItems>(new Uri(queueSettings.GrantItemsQueueAddress));
        EndpointConvention.Map<DebitGil>(new Uri(queueSettings.DebitGilQueueAddress));
        EndpointConvention.Map<SubtractItems>(new Uri(queueSettings.SubtractItemsQueueAddress));
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

