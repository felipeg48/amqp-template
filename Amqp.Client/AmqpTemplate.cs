using RabbitMQ.Client;
using Microsoft.Extensions.Logging;

namespace Amqp.Client // Updated namespace
{
    public class AmqpTemplate(
        string hostName = "localhost",
        int port = AmqpTcpEndpoint.UseDefaultPort,
        string userName = "guest",
        string password = "guest",
        ILogger<AmqpTemplate>? logger = null)
        : AbstractAmqpTemplate(hostName, port, userName, password, logger) // Renamed class
    {
        // --- Public Methods ---
        
        /// <summary>
        /// Sends a message to a specified exchange with a routing key.
        /// </summary>
        /// <param name="exchange">The exchange name.</param>
        /// <param name="routingKey">The routing key.</param>
        /// <param name="messageBody">The message content as a byte array.</param>
        /// <param name="mandatory">If true, the message will be returned to the publisher if it cannot be routed.</param>
        public void Send(string exchange, string routingKey, byte[] messageBody, bool mandatory = false)
        {
            try
            {
                if (Channel is { IsOpen: false })
                {
                    Logger.LogWarning("Channel is not open. Attempting to re-initialize connection/channel.");
                    _ = InitializeConnection(); 
                    if (!Channel.IsOpen)
                    {
                        Logger.LogError("Failed to re-establish channel. Message not sent.");
                        throw new InvalidOperationException("AmqpTemplate channel is not available.");
                    }
                }
        
                var properties = new BasicProperties
                {
                    Persistent = true
                };

                Channel?.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: routingKey,
                    mandatory: mandatory,
                    basicProperties: properties,
                    body: messageBody
                );
                Logger.LogInformation($"Message sent to exchange '{exchange}' with routing key '{routingKey}'.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to send message to exchange '{exchange}' with routing key '{routingKey}'.");
                throw;
            }
        }
    }
}
