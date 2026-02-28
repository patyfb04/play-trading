using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Play.Trading.Service.StatesMachine;

namespace Play.Trading.Service.Persistence
{

    public class PurchaseStateMap : SagaClassMap<PurchaseState>
    {
        protected override void Configure(EntityTypeBuilder<PurchaseState> entity, ModelBuilder model)
        {
            entity.Property(x => x.CurrentState);
            entity.Property(x => x.UserId);
            entity.Property(x => x.ItemId);
            entity.Property(x => x.Quantity);
        }
    }

}
