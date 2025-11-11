using MongoDB.Driver;
using OrderProcessing.Models;

namespace OrderProcessing.Data;

public interface IInventoryRepository
{
	// Fetch inventory item by Guid identifier.
	Task<InventoryItem?> GetByIdAsync(Guid id);
	// Insert a new inventory item.
	Task CreateAsync(InventoryItem item);
	// Atomically decrement AvailableQuantity if sufficient stock exists.
	Task<bool> TryReserveAsync(Guid id, int quantity);
}

public class InventoryRepository : IInventoryRepository
{
	private readonly IMongoCollection<InventoryItem> _collection;

	public InventoryRepository(IMongoContext context)
	{
		_collection = context.Database.GetCollection<InventoryItem>("inventory");
	}

	public async Task<InventoryItem?> GetByIdAsync(Guid id)
	{
		return await _collection.Find(x => x.Id.Equals(id)).FirstOrDefaultAsync();
	}

	public async Task CreateAsync(InventoryItem item)
	{
		await _collection.InsertOneAsync(item);
	}

	public async Task<bool> TryReserveAsync(Guid id, int quantity)
	{
		// Atomically decrement if enough stock using a single update filter.
		var filter = Builders<InventoryItem>.Filter.And(
			Builders<InventoryItem>.Filter.Eq(x => x.Id, id),
			Builders<InventoryItem>.Filter.Gte(x => x.AvailableQuantity, quantity)
		);
		var update = Builders<InventoryItem>.Update.Inc(x => x.AvailableQuantity, -quantity);
		var result = await _collection.UpdateOneAsync(filter, update);
		return result.ModifiedCount == 1;
	}
}


