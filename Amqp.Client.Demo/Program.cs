using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client; 

namespace Amqp.Client.Demo
{
    class Program
    {
        static async Task Main()
        {
            // Setup basic console logging for the application
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information); // Set the desired log level
            });
            var appLogger = loggerFactory.CreateLogger<Program>();

            // Instantiate AmqpTemplate with the logger
            using (var amqpTemplate = new AmqpTemplate(
                hostName: "localhost",
                userName: "guest", 
                password: "guest", 
                logger: loggerFactory.CreateLogger<AmqpTemplate>()))
            {
                // Subscribe to connection status changes
                amqpTemplate.ConnectionStatusChanged += (_, status) =>
                {
                    appLogger.LogInformation($"[AmqpTemplate Status Update]: {status}");
                };

                // Give some time for the connection to establish (important for async operations)
                await Task.Delay(2000); 

                const string exchangeName = "my-direct-exchange";
                const string routingKey = "my.routing.key";
                const string queueName = "my-queue";

                // --- Declare exchange and queue (typically done by consumer or setup script) ---
                // For this example, we'll declare them here for simplicity.
                // In a real app, this might be part of your infrastructure setup or consumer side.
                // Using the internal GetRawConnection for infrastructure setup only for the demo
                try
                {
                    var tempConnection = amqpTemplate.GetRawConnection();
                    var tempChannel = await tempConnection?.CreateChannelAsync()!;
                    await tempChannel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Direct, durable: true);
                    await tempChannel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                    await tempChannel.QueueBindAsync(queue: queueName, exchange: exchangeName, routingKey: routingKey);
                    appLogger.LogInformation($"Declared exchange '{exchangeName}' and queue '{queueName}', bound with routing key '{routingKey}'.");
                }
                catch (Exception ex)
                {
                    appLogger.LogError(ex, "Failed to declare exchange/queue. Ensure RabbitMQ is running and accessible.");
                    return; // Exit if basic setup fails
                }
                // -------------------------------------------------------------------------


                Console.WriteLine("\n--- Sending Messages ---");
                amqpTemplate.Send(exchangeName, routingKey, Encoding.UTF8.GetBytes("Hello RabbitMQ from the new solution!"));
                amqpTemplate.Send(exchangeName, "non.existent.key", Encoding.UTF8.GetBytes("This should be returned (from new solution)!"), mandatory: true); // Mandatory message

                Console.WriteLine("\n--- Testing Connection Recovery ---");
                Console.WriteLine("Try restarting your RabbitMQ server now. The client should attempt to reconnect.");
                Console.WriteLine("Press any key to send more messages after attempting recovery (give it 10-15 seconds).");
                Console.Read();

                amqpTemplate.Send(exchangeName, routingKey, Encoding.UTF8.GetBytes("Message after recovery attempt (from new solution)!"));

                Console.WriteLine("\nPress any key to exit.");
                Console.Read();
            }

            Console.WriteLine("Application exited.");
        }
    }
}
