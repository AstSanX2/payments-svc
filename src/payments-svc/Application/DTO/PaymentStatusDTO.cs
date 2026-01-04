namespace Application.DTO
{
    public class PaymentStatusDTO
    {
        public string PurchaseId { get; set; } = default!;
        public string Status { get; set; } = default!;
        public decimal? Amount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}

