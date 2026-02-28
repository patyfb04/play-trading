using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;


namespace Play.Trading.Service.Persistence
{
    public class PurchaseStateDbContext : SagaDbContext
    {
        public PurchaseStateDbContext(DbContextOptions options) : base(options) { }

        protected override IEnumerable<ISagaClassMap> Configurations
        {
            get { yield return new PurchaseStateMap(); }
        }
    }

}
