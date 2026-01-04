using Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController(IPaymentsService paymentsService) : ControllerBase
    {
        /// <summary>
        /// Obtém o status de um pagamento/compra.
        /// </summary>
        /// <param name="purchaseId">ID da compra (ObjectId)</param>
        [HttpGet("{purchaseId:length(24)}")]
        public async Task<IActionResult> GetPaymentStatus(string purchaseId, CancellationToken ct)
        {
            if (!ObjectId.TryParse(purchaseId, out var id))
                return BadRequest(new { error = "ID de compra inválido" });

            var result = await paymentsService.GetPaymentStatusAsync(id, ct);

            if (result is null)
                return NotFound(new { error = "Compra não encontrada" });

            return Ok(result);
        }

        /// <summary>
        /// Obtém o status simplificado de um pagamento (legacy endpoint).
        /// </summary>
        [HttpGet("/payments/{purchaseId}")]
        public async Task<IActionResult> GetPaymentStatusLegacy(string purchaseId, CancellationToken ct)
        {
            if (!ObjectId.TryParse(purchaseId, out var id))
                return BadRequest(new { error = "ID de compra inválido" });

            var result = await paymentsService.GetPaymentStatusAsync(id, ct);

            if (result is null)
                return NotFound(new { error = "not found" });

            return Ok(new { purchaseId, status = result.Status });
        }
    }
}

