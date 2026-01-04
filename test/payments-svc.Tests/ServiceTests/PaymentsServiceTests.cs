using Application.DTO;
using Application.Services;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;
using Moq;
using Xunit;

namespace payments_svc.Tests.ServiceTests
{
    public class PaymentsServiceTests
    {
        private readonly Mock<IPurchaseRepository> _mockPurchaseRepo;
        private readonly Mock<IEventRepository> _mockEventRepo;
        private readonly IPaymentsService _service;
        private readonly List<Purchase> _stubPurchases;

        public PaymentsServiceTests()
        {
            _mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            _mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);

            _stubPurchases = new List<Purchase>
            {
                new Purchase
                {
                    _id = ObjectId.GenerateNewId(),
                    GameId = ObjectId.GenerateNewId(),
                    UserId = ObjectId.GenerateNewId(),
                    Amount = 59.90m,
                    Status = "PAID",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    UpdatedAt = DateTime.UtcNow
                },
                new Purchase
                {
                    _id = ObjectId.GenerateNewId(),
                    GameId = ObjectId.GenerateNewId(),
                    UserId = ObjectId.GenerateNewId(),
                    Amount = 29.90m,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                }
            };

            // Setup event repository
            _mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Setup purchase repository
            _mockPurchaseRepo.Setup(r => r.GetByIdAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ObjectId id, CancellationToken ct) =>
                    _stubPurchases.FirstOrDefault(p => p._id == id));

            _service = new PaymentsService(_mockPurchaseRepo.Object, _mockEventRepo.Object);
        }

        [Fact(DisplayName = "GetPaymentStatusAsync deve retornar status quando compra existe")]
        public async Task GetPaymentStatusAsync_ExistingPurchase_ReturnsStatus()
        {
            // Arrange
            var purchase = _stubPurchases[0];

            // Act
            var result = await _service.GetPaymentStatusAsync(purchase._id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(purchase._id.ToString(), result.PurchaseId);
            Assert.Equal(purchase.Status, result.Status);
            Assert.Equal(purchase.Amount, result.Amount);

            _mockEventRepo.Verify(e =>
                e.AppendEventAsync(It.Is<DomainEvent>(ev => ev.Type == "PaymentStatusQueried"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact(DisplayName = "GetPaymentStatusAsync deve retornar null quando compra não existe")]
        public async Task GetPaymentStatusAsync_NonExistingPurchase_ReturnsNull()
        {
            // Arrange
            var nonExistingId = ObjectId.GenerateNewId();

            // Act
            var result = await _service.GetPaymentStatusAsync(nonExistingId);

            // Assert
            Assert.Null(result);

            _mockEventRepo.Verify(e =>
                e.AppendEventAsync(It.Is<DomainEvent>(ev => ev.Type == "PaymentStatusNotFound"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact(DisplayName = "GetPaymentStatusAsync deve retornar status PENDING para compra pendente")]
        public async Task GetPaymentStatusAsync_PendingPurchase_ReturnsStatusPending()
        {
            // Arrange
            var pendingPurchase = _stubPurchases[1];

            // Act
            var result = await _service.GetPaymentStatusAsync(pendingPurchase._id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("PENDING", result.Status);
        }

        [Fact(DisplayName = "GetPaymentStatusAsync deve retornar status PAID para compra paga")]
        public async Task GetPaymentStatusAsync_PaidPurchase_ReturnsStatusPaid()
        {
            // Arrange
            var paidPurchase = _stubPurchases[0];

            // Act
            var result = await _service.GetPaymentStatusAsync(paidPurchase._id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("PAID", result.Status);
            Assert.NotNull(result.UpdatedAt);
        }

        [Fact(DisplayName = "GetPaymentStatusAsync deve chamar repositório corretamente")]
        public async Task GetPaymentStatusAsync_CallsRepository()
        {
            // Arrange
            var purchaseId = _stubPurchases[0]._id;

            // Act
            await _service.GetPaymentStatusAsync(purchaseId);

            // Assert
            _mockPurchaseRepo.Verify(r => r.GetByIdAsync(purchaseId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "GetPaymentStatusAsync deve incluir dados de criação no resultado")]
        public async Task GetPaymentStatusAsync_IncludesCreatedAt()
        {
            // Arrange
            var purchase = _stubPurchases[0];

            // Act
            var result = await _service.GetPaymentStatusAsync(purchase._id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(purchase.CreatedAt, result.CreatedAt);
        }
    }
}

