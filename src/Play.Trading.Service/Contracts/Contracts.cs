namespace Play.Trading.Service.Contracts
{
    public record PurchaseRequested(
        Guid UserId, 
        Guid ItemId, 
        int Quantity, 
        Guid CorrelationId);

    public record GetPurchaseState(Guid CorrelationId);

    public record PurchaseCompleted(
     Guid UserId,
        Guid ItemId,
        decimal? PurchaseTotal,
        Guid CorrelationId);
}
