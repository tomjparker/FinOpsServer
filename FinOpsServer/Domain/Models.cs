namespace FinTrans.Domain;

public record PaymentRequest(string IdempotencyKey, string From, string To, decimal Amount, string Currency);
public record Payment(string Id, string From, string To, decimal Amount, string Currency, DateTimeOffset CreatedAt, string Status);