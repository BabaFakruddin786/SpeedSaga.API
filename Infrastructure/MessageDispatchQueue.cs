using System.Threading.Channels;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Infrastructure;

public record PendingMessageDispatch(Guid MessageId, string? PlainOtp = null);

/// <summary>Non-blocking outbound message dispatch — API returns immediately after queuing.</summary>
public interface IMessageDispatchQueue
{
    void Enqueue(Guid messageId, string? plainOtp = null);
}

public class MessageDispatchService : BackgroundService, IMessageDispatchQueue
{
    readonly Channel<PendingMessageDispatch> _channel;
    readonly IServiceScopeFactory _scopeFactory;
    readonly ILogger<MessageDispatchService> _log;

    public MessageDispatchService(IServiceScopeFactory scopeFactory, ILogger<MessageDispatchService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _channel = Channel.CreateBounded<PendingMessageDispatch>(new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Enqueue(Guid messageId, string? plainOtp = null)
    {
        var item = new PendingMessageDispatch(messageId, plainOtp);
        if (!_channel.Writer.TryWrite(item))
            _ = _channel.Writer.WriteAsync(item).AsTask();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var delivery = scope.ServiceProvider.GetRequiredService<IMessageDeliveryService>();
                await delivery.DispatchAsync(item.MessageId, item.PlainOtp, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Message dispatch failed for {MessageId}", item.MessageId);
            }
        }
    }
}
