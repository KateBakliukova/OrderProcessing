namespace OrderProcessing.Models;

public class OrderItem
{
	public Guid InventoryItemId { get; set; }
	public string Name { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
}


