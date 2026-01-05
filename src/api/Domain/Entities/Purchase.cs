using MongoDB.Bson;

namespace Domain.Entities
{
    public class Purchase
    {
        public ObjectId _id { get; set; }
        public ObjectId GameId { get; set; }
        public ObjectId UserId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "PENDING";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}

