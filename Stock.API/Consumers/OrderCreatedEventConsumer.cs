using MassTransit;
using Shared.OrderEvents;
using Stock.API.Services;
using MongoDB.Driver;
using Shared.Settings;
using Shared.StockEvents;

namespace Stock.API.Consumers
{
    public class OrderCreatedEventConsumer : IConsumer<OrderCreatedEvent>
    {
        private readonly MongoDBService mongoDBService;
        private readonly ISendEndpointProvider _sendEndpointProvider;

        public OrderCreatedEventConsumer(MongoDBService mongoDBService, ISendEndpointProvider sendEndpointProvider)
        {
            this.mongoDBService = mongoDBService;
            _sendEndpointProvider = sendEndpointProvider;
        }

        public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
        {
            List<bool> stockResults=new();
            var stockCollection=mongoDBService.GetCollection<Stock.API.Models.Stock>();

            foreach(var orderItem in context.Message.OrderItems)
                stockResults.Add(await (await stockCollection.FindAsync(s=>s.ProductId == orderItem.ProductId && s.Count>=(long)orderItem.Count)).AnyAsync());

            var sendEndPoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{RabbitMQSettings.StateMachineQueue}"));

            if (stockResults.TrueForAll(s => s.Equals(true)))
            {
                foreach (var orderItem in context.Message.OrderItems)
                {
                    var stock = await (await stockCollection.FindAsync(s => s.ProductId == orderItem.ProductId)).FirstOrDefaultAsync();

                    stock.Count -= orderItem.Count;

                    await stockCollection.FindOneAndReplaceAsync(x=>x.ProductId == orderItem.ProductId, stock);
                }

                StockReservedEvent stockReservedEvent = new(context.Message.CorrelationId)
                {
                    OrderItems = context.Message.OrderItems
                };

                await sendEndPoint.Send(stockReservedEvent);
            }
            else
            {
                StockNotReservedEvent stockNotReservedEvent = new(context.Message.CorrelationId)
                {
                    Message = "Stock yetersiz"
                };

                await sendEndPoint.Send(stockNotReservedEvent);
            }
        }
    }
}
