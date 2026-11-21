using System.Globalization;
using LingoCue.Llm.Providers;
using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Pipeline;

/// <summary>
/// One model call with a per-request timeout and back-off: rate limits, server errors and calls that
/// time out are retried after <see cref="PipelineOptions.TransientDelays"/>; the user's cancellation
/// always wins. Used by every pass (context and line batches) so they behave the same.
/// </summary>
internal static class ResilientChat
{
    public static async Task<ChatResponse> GetResponseAsync(
        IChatClient client,
        IList<ChatMessage> messages,
        ChatOptions chatOptions,
        PipelineOptions policy,
        Func<TimeSpan, CancellationToken, Task> delay,
        Action<string, TimeSpan>? onRetry,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (policy.Pacer is { } pacer)
            {
                await pacer.WaitAsync(delay, cancellationToken);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(policy.RequestTimeout);
            try
            {
                return await client.GetResponseAsync(messages, chatOptions, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The request outlived RequestTimeout (or the HTTP client gave up): retry, then report.
                if (attempt >= policy.TransientDelays.Count)
                {
                    throw new LlmException($"The model did not answer within {policy.RequestTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s, {attempt + 1} attempts.")
                    {
                        Remedy = "Dil modeli zamanında yanıt vermedi; Google şu an yoğun olabilir. Birkaç dakika sonra tekrar deneyin ya da Tercihler'den başka bir model seçin.",
                        IsUnanswered = true,
                    };
                }

                onRetry?.Invoke("Timeout", policy.TransientDelays[attempt]);
                await delay(policy.TransientDelays[attempt], cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && LineBatchRunner.IsTransient(ex) && attempt < policy.TransientDelays.Count)
            {
                // A rate limit says how long to wait; waiting less only burns another attempt.
                var wait = policy.TransientDelays[attempt];
                if (RetryHint.From(ex) is { } hint && hint > wait)
                {
                    wait = hint < policy.MaxRetryWait ? hint : policy.MaxRetryWait;
                }

                onRetry?.Invoke(ex.GetType().Name, wait);
                await delay(wait, cancellationToken);
            }
        }
    }
}
