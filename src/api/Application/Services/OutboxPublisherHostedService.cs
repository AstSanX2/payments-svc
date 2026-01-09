using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace Application.Services
{
    public class OutboxPublisherHostedService : BackgroundService
    {
        private static readonly ActivitySource ActivitySource = new("payments-api.outbox");
        private readonly IOutboxRepository _outbox;
        private readonly IConfiguration _configuration;
        private IAmazonSQS? _sqs;

        public OutboxPublisherHostedService(IOutboxRepository outbox, IConfiguration configuration)
        {
            _outbox = outbox;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = await _outbox.DequeueBatchAsync(limit: 10, nowUtc: DateTime.UtcNow, ct: stoppingToken);
                    if (batch.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    foreach (var msg in batch)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        try
                        {
                            _sqs ??= CreateSqsClient(_configuration);
                            if (_sqs is null)
                            {
                                await _outbox.MarkFailedAsync(msg, "SQS client not configured (missing Sqs:ServiceUrl or AWS:Region).", DateTime.UtcNow.AddMinutes(1), stoppingToken);
                                continue;
                            }

                            using var activity = StartProducerActivity(msg);

                            var resp = await _sqs.SendMessageAsync(new SendMessageRequest
                            {
                                QueueUrl = msg.Destination,
                                MessageBody = msg.Body
                            }, stoppingToken);

                            activity?.SetTag("messaging.message_id", resp.MessageId);

                            await _outbox.MarkPublishedAsync(msg, resp.MessageId, DateTime.UtcNow, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            var next = ComputeBackoffUtc(msg.Attempts);
                            await _outbox.MarkFailedAsync(msg, ex.Message, next, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private static DateTime ComputeBackoffUtc(int attempts)
        {
            var seconds = Math.Min(300, (int)Math.Pow(2, Math.Clamp(attempts, 0, 8)));
            return DateTime.UtcNow.AddSeconds(seconds);
        }

        private static IAmazonSQS? CreateSqsClient(IConfiguration configuration)
        {
            var serviceUrl = configuration["Sqs:ServiceUrl"];
            if (!string.IsNullOrEmpty(serviceUrl))
            {
                var config = new AmazonSQSConfig { ServiceURL = serviceUrl };
                var accessKey = configuration["AWS:AccessKey"];
                var secretKey = configuration["AWS:SecretKey"];
                if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
                    return new AmazonSQSClient(new BasicAWSCredentials(accessKey, secretKey), config);

                return new AmazonSQSClient(config);
            }

            var region = configuration["AWS:Region"];
            if (string.IsNullOrWhiteSpace(region))
                return null;

            var sqsConfig = new AmazonSQSConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(region)
            };

            var ak = configuration["AWS:AccessKey"];
            var sk = configuration["AWS:SecretKey"];
            if (!string.IsNullOrWhiteSpace(ak) && !string.IsNullOrWhiteSpace(sk))
                return new AmazonSQSClient(new BasicAWSCredentials(ak, sk), sqsConfig);

            return new AmazonSQSClient(sqsConfig);
        }

        private static Activity? StartProducerActivity(OutboxMessage msg)
        {
            try
            {
            string? correlationId = msg.CorrelationId;
                string? eventType = msg.EventType;
            string? destination = msg.Destination;
            var eventId = msg.EventId;

                Activity? activity;

                if (!string.IsNullOrWhiteSpace(correlationId)
                    && ActivityContext.TryParse(correlationId, null, out var parentContext))
                {
                    activity = ActivitySource.StartActivity("outbox publish", ActivityKind.Producer, parentContext);
                }
                else
                {
                    activity = ActivitySource.StartActivity("outbox publish", ActivityKind.Producer);
                }

                activity?.SetTag("messaging.system", "aws.sqs");
                activity?.SetTag("messaging.destination", destination);
                activity?.SetTag("messaging.operation", "send");
                if (!string.IsNullOrWhiteSpace(eventType)) activity?.SetTag("fcg.event_type", eventType);
                activity?.SetTag("fcg.event_id", eventId.ToString());

                return activity;
            }
            catch
            {
                return null;
            }
        }
    }
}


