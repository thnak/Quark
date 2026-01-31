using Microsoft.Extensions.Logging;
using Quark.Sagas;

namespace Quark.Examples.Sagas;

/// <summary>
/// Saga step that processes payment for an order.
/// </summary>
public class ProcessPaymentStep : ISagaStep<OrderContext>
{
    private readonly ILogger _logger;
    private readonly bool _shouldFail;

    public string Name => "ProcessPayment";

    public ProcessPaymentStep(ILogger logger, bool shouldFail = false)
    {
        _logger = logger;
        _shouldFail = shouldFail;
    }

    public async Task ExecuteAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing payment for order {OrderId}, amount ${Amount}",
            context.OrderId, context.Amount);

        // Simulate payment processing
        await Task.Delay(100, cancellationToken);

        if (_shouldFail)
        {
            _logger.LogError("Payment processing failed for order {OrderId}", context.OrderId);
            throw new InvalidOperationException("Payment gateway returned error: Insufficient funds");
        }

        // Payment successful
        context.PaymentTransactionId = Guid.NewGuid().ToString("N")[..12];
        context.PaymentCompleted = true;

        _logger.LogInformation("Payment processed successfully. Transaction ID: {TransactionId}",
            context.PaymentTransactionId);
    }

    public async Task CompensateAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Refunding payment for order {OrderId}, transaction {TransactionId}",
            context.OrderId, context.PaymentTransactionId);

        // Simulate refund processing
        await Task.Delay(100, cancellationToken);

        context.PaymentCompleted = false;
        _logger.LogInformation("Payment refunded successfully for order {OrderId}", context.OrderId);
    }
}
