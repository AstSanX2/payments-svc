namespace Application.DTO
{
    /// <summary>
    /// Mensagem publicada pelo games-svc na fila de pagamentos.
    /// </summary>
    public record PurchaseMessageDTO(string PurchaseId, string UserId, decimal Amount);
}


