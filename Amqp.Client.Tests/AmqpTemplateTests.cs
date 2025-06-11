using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Amqp.Client.Tests
{
    public class AmqpTemplateTests
    {
        private readonly ILogger<AmqpTemplate>? _testLogger;
        private AmqpTemplate? _amqpTemplate;

        public AmqpTemplateTests()
        {
            // Optional: Set up a logger for tests if you want output during test runs
            // In a real test, you might use a mock logger.
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            _testLogger = loggerFactory.CreateLogger<AmqpTemplate>();
        }

        // IAsyncLifetime.InitializeAsync - Called before each test method
        internal async Task InitializeAsync()
        {
            _amqpTemplate = new AmqpTemplate(
                hostName: "localhost", // Ensure RabbitMQ is running locally for this test
                userName: "guest",
                password: "guest",
                logger: _testLogger);

            // Give the template some time to establish its initial connection
            // In a real integration test suite; you might have a more robust
            // way to ensure the connection is ready, e.g., a "ConnectAsync" method
            // on AmqpTemplate that returns a Task.
            await Task.Delay(2000); 

            // Basic check to ensure connection is likely established before proceeding
            if (_amqpTemplate.GetRawConnection() == null || _amqpTemplate.GetRawConnection() is not { IsOpen: true })
            {
                _testLogger!.LogWarning("Connection was not open after initial delay. Test might be flaky.");
                // Potentially throw an exception or skip the test if the connection isn't ready.
            }
        }

        // IAsyncLifetime.DisposeAsync - Called after each test method
        internal async Task DisposeAsync()
        {
            _amqpTemplate?.Dispose();
            await Task.CompletedTask; // Since Dispose() is synchronous
        }
        
        [Fact]
        public void AmqpTemplate_CanBeInstantiated()
        {
            // Arrange & Act
            using var template = new AmqpTemplate(logger: _testLogger);
            // Assert
            Assert.NotNull(template);
        }

        [Fact]
        public async Task AmqpTemplate_ConnectionShutdownEvent_FiresOnConnectionClose()
        {
            // Arrange
            // InitializeAsync already created _amqpTemplate and connected it
            await InitializeAsync();
            
            // Use TaskCompletionSource to wait for the event
            var connectionShutdownTcs = new TaskCompletionSource<string>();

            _amqpTemplate!.ConnectionStatusChanged += (_, status) =>
            {
                Assert.True(status.Contains("Connection Shutdown"), "Expected status to indicate connection shutdown.");
                if (status.StartsWith("Connection Shutdown"))
                {
                    connectionShutdownTcs.TrySetResult(status);
                }
            };

            // Act
            // Force the underlying connection to close (this simulates a shutdown)
            var rawConnection = _amqpTemplate.GetRawConnection();
            Assert.NotNull(rawConnection);
            Assert.True(rawConnection.IsOpen, "Raw connection should be open before simulating shutdown.");
            
            const ushort closeCode = 320; // Example: PROTOCOL_ERROR (often used for client-initiated close)
            const string closeText = "Simulated client close for test";
            
            if (rawConnection is { IsOpen: true })
            {
                _testLogger!.LogInformation("Forcing RabbitMQ connection to close...");
                await rawConnection.CloseAsync(closeCode, closeText,TimeSpan.FromSeconds(5), false); // Use the async Close method
                _testLogger!.LogInformation("Connection closed.");
            }
            
            // Assert
            // Wait for the event to be captured, with a timeout
            var receivedStatus = await connectionShutdownTcs.Task.WithTimeout(TimeSpan.FromSeconds(10)); // Custom extension for timeout

            Assert.StartsWith("Connection Shutdown", receivedStatus);
            _testLogger!.LogInformation($"Received shutdown status: {receivedStatus}");
        }
        
        [Fact]
        public async Task AmqpTemplate_ChannelShutdownEvent_FiresOnChannelClose()
        {
            // Arrange
            // InitializeAsync already created _amqpTemplate and connected it
            await InitializeAsync();
            
            var rawChannel = _amqpTemplate!.GetRawChannel();
            Assert.NotNull(rawChannel); // Ensure channel is not null
            Assert.True(rawChannel.IsOpen); // Ensure the channel is open

            // Use TaskCompletionSource to wait for the event
            var channelShutdownTcs = new TaskCompletionSource<ShutdownEventArgs>();

            // Subscribe to the direct RabbitMQ.Client ChannelShutdownAsync event
            rawChannel.ChannelShutdownAsync += (_, args) =>
            {
                _testLogger!.LogInformation($"[TEST] Direct ChannelShutdownAsync fired with ReplyCode: {args.ReplyCode}, ReplyText: {args.ReplyText}");
                channelShutdownTcs.TrySetResult(args);
                return Task.CompletedTask;
            };

            // Act
            _testLogger!.LogInformation("Forcing RabbitMQ channel to close with specific code and text...");
            const ushort closeCode = 200; // AMQP.Constants.ReplySuccess is common for client-initiated close
            const string closeText = "Simulated client channel close for test";

            // IChannel.CloseAsync signature: CloseAsync(ushort reasonCode, string reasonText, bool abort, CancellationToken cancellationToken)
            await rawChannel.CloseAsync(closeCode, closeText, false); 
            _testLogger!.LogInformation("CloseAsync called on channel.");

            // Assert
            // Wait for the event to be captured, with a timeout
            var receivedShutdownArgs = await channelShutdownTcs.Task.WithTimeout(TimeSpan.FromSeconds(10)); 

            Assert.Equal(closeCode, receivedShutdownArgs.ReplyCode);
            Assert.Equal(closeText, receivedShutdownArgs.ReplyText);
            Assert.Equal(ShutdownInitiator.Application, receivedShutdownArgs.Initiator); // Should be Application for client-initiated close

            _testLogger!.LogInformation("AmqpTemplate_ChannelShutdownEvent_FiresOnChannelClose test passed.");
        }
        
        
    }
    
    
    public static class TaskExtensions
    {
        public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
            {
                throw new TimeoutException($"The operation timed out after {timeout.TotalSeconds} seconds.");
            }
            return await task; // Await the original task to propagate any exceptions
        }
    }
}
