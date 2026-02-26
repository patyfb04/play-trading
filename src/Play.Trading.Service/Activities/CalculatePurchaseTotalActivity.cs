using Automatonymous;
using MassTransit;
using Play.Common.Repositories;
using Play.Trading.Service.Contracts;
using Play.Trading.Service.Entities;
using Play.Trading.Service.Exceptions;
using Play.Trading.Service.StatesMachine;

namespace Play.Trading.Service.Activities
{
    // to be moved tyo another microservice, to process transations
    public class CalculatePurchaseTotalActivity : IStateMachineActivity<PurchaseState, PurchaseRequested>
    {
        public readonly IRepository<CatalogItem> _repository;
        public CalculatePurchaseTotalActivity(IRepository<CatalogItem> repository)
        {
            _repository = repository;
        }

        public void Accept(StateMachineVisitor visitor)
        {
            visitor.Visit(this);
        }

        public async Task Execute(BehaviorContext<PurchaseState, PurchaseRequested> context, IBehavior<PurchaseState, PurchaseRequested> next)
        {
            var message = context.Message;

            var item = await _repository.GetAsync(message.ItemId);

            if (item == null) {
                throw new UnknownItemException(message.ItemId);
                
            }

            context.Saga.PurchaseTotal = item.Price * message.Quantity;
            context.Saga.LastUpdated = DateTimeOffset.UtcNow;

            await next.Execute(context);
        }

        public Task Faulted<TException>(BehaviorExceptionContext<PurchaseState, PurchaseRequested, TException> context, IBehavior<PurchaseState, PurchaseRequested> next) where TException : Exception
        {
           return  next.Faulted(context);
        }

        public void Probe(ProbeContext context)
        {
            context.CreateScope("calculate-purchase-total");
        }
    }
}
