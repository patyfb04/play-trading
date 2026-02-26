namespace Play.Trading.Service.Dtos
{
    public record InventoryItemDto(Guid Id, Guid CatalogItemId, Guid UserId, string Name, string Description, int Quantity, DateTimeOffset AcquiredDate);

    public record CatalogItemDto(Guid Id, string Name, string Description)
    {
        public decimal Price { get; internal set; }
    }

    public record UserDto(
       Guid Id,
       string Username,
       string Email,
       decimal Gil,
       DateTimeOffset CreatedDate);
}
