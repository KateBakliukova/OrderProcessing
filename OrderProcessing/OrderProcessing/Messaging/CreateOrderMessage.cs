namespace OrderProcessing.Messaging;

public record CreateOrderMessage(
	Guid OrderId,
	string CustomerId,
	List<CreateOrderItemRefMessage> Items,
	string? PromoCode
);

public record CreateOrderItemRefMessage(
	Guid InventoryItemId,
	int Quantity
);


