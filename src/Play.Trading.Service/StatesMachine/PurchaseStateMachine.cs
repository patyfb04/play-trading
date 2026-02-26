using MassTransit;
using MassTransit.SqlTransport.Topology;
using Microsoft.Extensions.Options;
using Play.Identity.Contracts;
using Play.Inventory.Contracts;
using Play.Trading.Service.Activities;
using Play.Trading.Service.Contracts;
using Play.Trading.Service.Settings;
using Play.Trading.Service.SignalR;

namespace Play.Trading.Service.StatesMachine
{
    public class PurchaseStateMachine : MassTransitStateMachine<PurchaseState>
    {

        public readonly QueueSettings _settings;
        private readonly MessageHub _hub;
        public State Accepted { get; set; }

        public State ItemsGranted { get; set; }

        public State Completed { get; set; }

        public State Faulted { get; set; }

        public Event<PurchaseRequested> PurchaseRequested { get; }
        public Event<GetPurchaseState> GetPurchaseState { get; }
        public Event<InventoryItemsGranted> InventoryItemsGranted { get; }
        public Event<GilDebited> GilDebited { get; }

        public Event<Fault<PurchaseRequested>> PurchaseRequestedFaulted { get; private set; }
        public Event<Fault<InventoryItemsGranted>> InventoryItemsGrantedFaulted { get; private set; }
        public Event<Fault<GrantItems>> GrantItemsFaulted { get; private set; }
        public Event<Fault<DebitGil>> DebitGilFaulted { get; private set; }


        public PurchaseStateMachine(
            IOptions<QueueSettings> settings, 
            MessageHub _hub)
        {
            _settings = settings.Value;
            this._hub = _hub;
            InstanceState(state => state.CurrentState);
            ConfigureEvents();
            ConfigureInitialState();
            ConfigureAccepted();
            ConfigureItemsGranted();
            ConfigureAny();
            ConfigureFaulted();
            ConfigureCompleted();
        }

        private void ConfigureEvents()
        {
            Event(() => PurchaseRequested, x => x.CorrelateById(context => context.Message.CorrelationId));
            Event(() => GetPurchaseState, x => x.CorrelateById(context => context.Message.CorrelationId));
            Event(() => InventoryItemsGranted, x => x.CorrelateById(context => context.Message.CorrelationId));
            Event(() => GilDebited, x => x.CorrelateById(context => context.Message.CorrelationId));
            Event(() => PurchaseRequestedFaulted, x => x.CorrelateById(context => context.Message.Message.CorrelationId));
            Event(() => InventoryItemsGrantedFaulted, x => x.CorrelateById(context => context.Message.Message.CorrelationId));
            Event(() => DebitGilFaulted, x => x.CorrelateById(context => context.Message.Message.CorrelationId));
            Event(() => GrantItemsFaulted, x => x.CorrelateById(context => context.Message.Message.CorrelationId));
        }

        private void ConfigureInitialState()
        {
            Initially(
                When(PurchaseRequested)
                .Then(context =>
                {
                    context.Saga.UserId = context.Message.UserId;
                    context.Saga.ItemId = context.Message.ItemId;
                    context.Saga.Quantity = context.Message.Quantity;
                    context.Saga.Received = DateTimeOffset.Now;
                    context.Saga.LastUpdated = context.Saga.Received;
                })
                .Activity(x=> x.OfType<CalculatePurchaseTotalActivity>()) // migrate to a microservice later
                .Send(new Uri(_settings.GrantItemsQueueAddress), context =>
                        new GrantItems(
                            context.Saga.UserId,
                            context.Saga.ItemId,
                            context.Saga.Quantity,
                            context.Saga.CorrelationId)
                    )
                .TransitionTo(Accepted));
        }

        private void ConfigureAccepted()
        {
            During(Accepted,
                Ignore(PurchaseRequested),
                When(InventoryItemsGranted)
                    .Then(context =>
                    {
                        context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                    })
                    .Send(new Uri(_settings.DebitGilQueueAddress), context =>
                            new DebitGil(
                                context.Saga.UserId,
                                context.Saga.PurchaseTotal!.Value,
                                context.Saga.CorrelationId)
                        )
                    .TransitionTo(ItemsGranted),
                When(GrantItemsFaulted)
                    .Then(context =>
                    {
                        context.Saga.ErrorMessage = string.Join(",", context.Message.Exceptions.Select(e => e.Message));
                        context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                    })
                    .TransitionTo(Faulted)
                    .ThenAsync(async context =>
                     {
                         await _hub.SendStatusAsync(context.Saga);
                     })
                );
        }

        private void ConfigureItemsGranted()
        {
            During(ItemsGranted,
               Ignore(PurchaseRequested),
               Ignore(InventoryItemsGranted),
               When(GilDebited)
                   .Then(context =>
                   {
                       context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                   })
                   .Publish(context =>
                         new PurchaseCompleted(
                               context.Saga.UserId,
                               context.Saga.ItemId,
                               context.Saga.PurchaseTotal!.Value,
                               context.Saga.CorrelationId))
                   .TransitionTo(Completed)
                    .ThenAsync(async context =>
                    {
                        await _hub.SendStatusAsync(context.Saga);
                    }),

               When(DebitGilFaulted)
                .Publish(context =>
                            new SubtractItems(
                                context.Saga.UserId,
                                context.Saga.ItemId,
                                context.Saga.Quantity,
                                context.Saga.CorrelationId))
                 .Then(context =>
                 {
                     context.Saga.ErrorMessage = string.Join(",", context.Message.Exceptions.Select(e => e.Message));
                     context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                 })
                .TransitionTo(Faulted)
                .ThenAsync(async context =>
                {
                    await _hub.SendStatusAsync(context.Saga);
                })
               );
        }

        private void ConfigureCompleted()
        {
            During(Completed,
                Ignore(PurchaseRequested),
                Ignore(InventoryItemsGranted),
                Ignore(GilDebited)

            );
        }

        private void ConfigureAny()
        {
            DuringAny(
                When(GetPurchaseState)
                    .Respond(x => x.Saga)
            );

            DuringAny(
                When(PurchaseRequestedFaulted)
                    .Then(context =>
                    {
                        context.Saga.ErrorMessage = string.Join(",",context.Message.Exceptions.Select(c=> c.Message));
                        context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                    })
                    .ThenAsync(async context =>
                     {
                         await _hub.SendStatusAsync(context.Saga);
                     })
            );

          DuringAny(
            When(InventoryItemsGrantedFaulted)
                .Then(context =>
                {
                    context.Saga.ErrorMessage = string.Join(",", context.Message.Exceptions.Select(c => c.Message));
                    context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                })
                .ThenAsync(async context =>
                 {
                     await _hub.SendStatusAsync(context.Saga);
                 })
            );

            DuringAny(
                 When(DebitGilFaulted)
                     .Then(context =>
                     {
                         context.Saga.ErrorMessage = string.Join(",", context.Message.Exceptions.Select(e => e.Message));
                         context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                     })
                     .TransitionTo(Faulted)
                     .ThenAsync(async context =>
                      {
                          await _hub.SendStatusAsync(context.Saga);
                      })
             );


        }

        private void ConfigureFaulted()
        {
               During(Faulted,
                    Ignore(PurchaseRequested),
                    Ignore(InventoryItemsGranted),
                    Ignore(GilDebited)
            );
        }
    }
}
