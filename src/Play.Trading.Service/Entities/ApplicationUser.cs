using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Play.Common.Entities;

namespace Play.Trading.Service.Entities
{
    public class ApplicationUser : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public Guid Id { get; set; }
        
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Gil { get; set; }
    }
}
