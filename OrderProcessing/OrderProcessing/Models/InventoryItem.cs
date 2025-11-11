using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OrderProcessing.Models;

public class InventoryItem
{
	[BsonId]
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public int AvailableQuantity { get; set; }
	public decimal UnitPrice { get; set; }
}


