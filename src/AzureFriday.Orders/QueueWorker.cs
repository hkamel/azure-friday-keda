using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AzureFriday.Orders
{
    public abstract class QueueWorker<TMessage> : BackgroundService
    {
        private readonly ServiceBusConnectionStringBuilder _serviceBusConnectionStringBuilder;
        protected ILogger<QueueWorker<TMessage>> Logger { get; }
        protected IConfiguration Configuration { get; }

        protected QueueWorker(ServiceBusConnectionStringBuilder serviceBusConnectionStringBuilder, IConfiguration configuration, ILogger<QueueWorker<TMessage>> logger)
        {
            _serviceBusConnectionStringBuilder = serviceBusConnectionStringBuilder;
            Configuration = configuration;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var ordersQueue = Configuration.GetValue<string>("SERVICEBUS_QUEUE_ORDERS");
            var queueClient = new QueueClient(_serviceBusConnectionStringBuilder.GetNamespaceConnectionString(), ordersQueue, ReceiveMode.PeekLock);

            Logger.LogInformation("Starting message pump");
            queueClient.RegisterMessageHandler(HandleMessage, HandleReceivedException);
            Logger.LogInformation("Message pump started");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Logger.LogInformation("Closing message pump");
            await queueClient.CloseAsync();
            Logger.LogInformation("Message pump closed : {Time}", DateTimeOffset.UtcNow);
        }

        private Task HandleReceivedException(ExceptionReceivedEventArgs exceptionEvent)
        {
            Logger.LogError(exceptionEvent.Exception, "Unable to process message");
            return Task.CompletedTask;
        }

        protected abstract Task ProcessMessage(TMessage order, string messageId, Message.SystemPropertiesCollection systemProperties, IDictionary<string, object> userProperties, CancellationToken cancellationToken);

        private async Task HandleMessage(Message message, CancellationToken cancellationToken)
        {
            var rawMessageBody = Encoding.UTF8.GetString(message.Body);
            Logger.LogInformation("Received message {MessageId} with body {MessageBody}", message.MessageId, rawMessageBody);

            var order = JsonConvert.DeserializeObject<TMessage>(rawMessageBody);
            if (order != null)
            {
                await ProcessMessage(order, message.MessageId, message.SystemProperties, message.UserProperties, cancellationToken);
            }
            else
            {
                Logger.LogError("Unable to deserialize to message contract {ContractName} for message {MessageBody}", typeof(TMessage), rawMessageBody);
            }

            Logger.LogInformation("Message {MessageId} processed", message.MessageId);
        }
    }
}
