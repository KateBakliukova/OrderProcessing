using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OrderProcessing.Messaging;

public interface IOrderQueuePublisher
{
	void PublishOrder(CreateOrderMessage message);
}

public class OrderQueuePublisher : IOrderQueuePublisher, IDisposable
{
	private readonly IConnection _connection;
	private readonly IModel _channel;
	private readonly string _queueName;

	public OrderQueuePublisher(IOptions<RabbitSettings> options)
	{
		var factory = new ConnectionFactory
		{
			HostName = options.Value.HostName,
			Port = options.Value.Port,
			UserName = options.Value.UserName,
			Password = options.Value.Password,
			DispatchConsumersAsync = true
		};

		_connection = factory.CreateConnection();
		_channel = _connection.CreateModel();
		_queueName = options.Value.QueueName;

		_channel.QueueDeclare(queue: _queueName,
			durable: true,
			exclusive: false,
			autoDelete: false,
			arguments: null);
	}

	public void PublishOrder(CreateOrderMessage message)
	{
		var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
		var props = _channel.CreateBasicProperties();
		props.DeliveryMode = 2; // persistent
		_channel.BasicPublish(exchange: "",
			routingKey: _queueName,
			basicProperties: props,
			body: body);
	}

	public void Dispose()
	{
		if (_channel.IsOpen) _channel.Close();
		_channel.Dispose();
		_connection.Dispose();
	}
}


