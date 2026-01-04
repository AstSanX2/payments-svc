namespace Domain.Interfaces.Services
{
    public interface IPaymentProcessingService
    {
        /// <summary>
        /// Processa uma mensagem vinda da SQS, garantindo idempotência por SqsMessageId.
        /// </summary>
        Task ProcessAsync(string sqsMessageId, string messageBody, CancellationToken ct = default);
    }
}


