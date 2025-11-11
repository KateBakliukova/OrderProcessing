public record CreateOrderRequest(string CustomerId, List<CreateOrderItem> Items, string? PromoCode);
public record CreateOrderItem(Guid InventoryItemId, int Quantity);

public record InventoryCreateRequest(string Name, decimal UnitPrice, int AvailableQuantity);


