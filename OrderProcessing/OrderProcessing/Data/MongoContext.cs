using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace OrderProcessing.Data;

public class MongoSettings
{
	public string ConnectionString { get; set; } = "mongodb://localhost:27017";
	public string Database { get; set; } = "orderdb";
}

public interface IMongoContext
{
	IMongoDatabase Database { get; }
}

public class MongoContext : IMongoContext
{
	public IMongoDatabase Database { get; }

	public MongoContext(IOptions<MongoSettings> options)
	{
		var mongoClient = new MongoClient(options.Value.ConnectionString);
		Database = mongoClient.GetDatabase(options.Value.Database);
	}
}


