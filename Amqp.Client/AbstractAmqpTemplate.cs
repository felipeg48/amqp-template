using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions; 

namespace Amqp.Client
{
    public abstract class AbstractAmqpTemplate : IDisposable // Changed to abstract
    {
        protected readonly ConnectionFactory ConnectionFactory; // Changed to protected
        protected IConnection? Connection; // Changed to protected
        protected IChannel? Channel; // Changed to protected
        protected readonly ILogger<AbstractAmqpTemplate> Logger; // Updated generic type, changed to protected

        public event EventHandler<string>? ConnectionStatusChanged;

        // Constructor
        protected AbstractAmqpTemplate(string hostName = "localhost", int port = AmqpTcpEndpoint.UseDefaultPort, string userName = "guest", string password = "guest", ILogger<AbstractAmqpTemplate>? logger = null)
        {
            ConnectionFactory = new ConnectionFactory()
            {
                HostName = hostName,
                Port = port,
                UserName = userName,
                Password = password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };
            Logger = logger ?? NullLoggerFactory.Instance.CreateLogger<AbstractAmqpTemplate>();

            InitializeConnection()
                .ContinueWith(task =>
                {
                    if (!task.IsFaulted) return;
                    Logger.LogError(task.Exception, "Failed to initialize AMQP connection.");
                    ConnectionStatusChanged?.Invoke(this, "Failed to initialize AMQP connection.");
                });
        }

        protected async Task InitializeConnection() // Changed to protected
        {
            try
            {
                Connection = await ConnectionFactory.CreateConnectionAsync();
                ConnectionStatusChanged?.Invoke(this, $"Connection established to {ConnectionFactory.HostName}:{ConnectionFactory.Port}");
                Logger.LogInformation($"Connection established to {ConnectionFactory.HostName}:{ConnectionFactory.Port}");

                Connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
                Connection.CallbackExceptionAsync += OnCallbackExceptionAsync;
                Connection.ConnectionBlockedAsync += OnConnectionBlockedAsync;
                Connection.ConnectionUnblockedAsync += OnConnectionUnblockedAsync;

                Channel = await Connection.CreateChannelAsync();
                Logger.LogInformation("Channel created.");
                
                Channel.ChannelShutdownAsync += OnChannelShutdownAsync;
                // Add BasicReturnAsync and FlowControlAsync to channel events if needed in base class
                // _channel.BasicReturnAsync += OnBasicReturnAsync; // These would be in AmqpTemplate if specific to send/consume
                // _channel.FlowControlAsync += OnFlowControlAsync;
            }
            catch (Exception ex)
            {
                ConnectionStatusChanged?.Invoke(this, $"Failed to establish connection: {ex.Message}");
                Logger.LogError(ex, "Failed to establish initial connection.");
                // Note: AutomaticRecoveryEnabled will try to reconnect
            }
        }

        // --- Connection Event Handlers (remain as private or protected) ---
        protected Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs @event)
        {
            ConnectionStatusChanged?.Invoke(this, $"Connection Shutdown: {@event.ReplyText} ({@event.Cause})");
            Logger.LogWarning($"Connection Shutdown: {@event.ReplyText} ({@event.Cause}). Initiated by: {@event.Initiator}");
            return Task.CompletedTask;
        }

        protected Task OnCallbackExceptionAsync(object sender, CallbackExceptionEventArgs @event)
        {
            ConnectionStatusChanged?.Invoke(this, $"Callback Exception: {@event.Exception.Message}");
            Logger.LogError(@event.Exception, "Callback Exception occurred in connection.");
            return Task.CompletedTask;
        }

        protected Task OnConnectionBlockedAsync(object sender, ConnectionBlockedEventArgs @event)
        {
            ConnectionStatusChanged?.Invoke(this, $"Connection Blocked: {@event.Reason}");
            Logger.LogWarning($"Connection Blocked: {@event.Reason}");
            return Task.CompletedTask;
        }

        protected Task OnConnectionUnblockedAsync(object sender, AsyncEventArgs @event)
        {
            ConnectionStatusChanged?.Invoke(this, "Connection Unblocked.");
            Logger.LogInformation("Connection Unblocked.");
            return Task.CompletedTask;
        }

        // --- Channel Event Handlers (remain as private or protected) ---
        protected Task OnChannelShutdownAsync(object sender, ShutdownEventArgs @event)
        {
            ConnectionStatusChanged?.Invoke(this, $"Channel Shutdown: {@event.ReplyText} ({@event.Cause})");
            Logger.LogWarning($"Channel Shutdown: {@event.ReplyText} ({@event.Cause}). Initiated by: {@event.Initiator}");
            return Task.CompletedTask;
        }

        // Internal helpers for access to raw connection/channel (for demo/tests)
        public IConnection? GetRawConnection() 
        {
            return Connection; 
        }

        public IChannel? GetRawChannel() 
        {
            return Channel; 
        }
        
        // IDisposable implementation for proper cleanup
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Channel != null && Channel!.IsOpen)
                {
                    Channel.CloseAsync(200, "Closing channel via Dispose").GetAwaiter().GetResult(); // Synchronous close for Dispose
                    Channel.Dispose();
                    Logger.LogInformation("Channel disposed.");
                }
                
                if (Connection != null && Connection!.IsOpen)
                {
                    Connection.CloseAsync(200, "Closing connection via Dispose").GetAwaiter().GetResult(); // Synchronous close for Dispose
                    Connection.Dispose();
                    Logger.LogInformation("Connection disposed.");
                }
            }
        }
    }
}