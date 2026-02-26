using MongoDB.Driver;
using Play.Common.Repositories;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Repository
{
    public class TradingUserRepository
    : MongoRepository<ApplicationUser>, ITradingUserRepository
    {
        public TradingUserRepository(IMongoDatabase database, string collectionName)
            : base(database, collectionName)
        {
        }
    }
}
