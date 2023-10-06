using Polly;
using Polly.Retry;
using Polly.Timeout;

try
{
    await TimeoutStrategySampleAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Unhandled exception: {ex.Message}");
}

try
{
    await RetryStrategySampleAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Unhandled exception: {ex.Message}");
}

try
{
    await CompositeStrategySampleAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Unhandled exception: {ex.Message}");
}

Console.WriteLine("Done!");

async Task TimeoutStrategySampleAsync()
{
    var pipeline = new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            OnTimeout = args =>
            {
                Console.WriteLine($"Timeout occurred after: {args.Timeout.TotalSeconds} seconds");
                return default;
            }
        })
        .Build();

    var result = await pipeline.ExecuteAsync(async (token) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(10), token);
        return 42;
    }, CancellationToken.None);
}

async Task RetryStrategySampleAsync()
{
    var pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            DelayGenerator = args =>
            {
                return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber + 1)));
            },
            ShouldHandle = new PredicateBuilder().Handle<ApplicationException>()
                .Handle<InvalidOperationException>(ex => ex.Message == "Something went wrong"),
            //ShouldHandle = args=>args.Outcome.Exception switch
            //{
            //    ApplicationException _ => PredicateResult.True(),
            //    _ => PredicateResult.False()
            //}
            OnRetry = args =>
            {
                Console.WriteLine($"Retrying... {args.AttemptNumber + 1} attempt after {args.RetryDelay}");
                return default;
            }
        })
        .Build();

    await pipeline.ExecuteAsync(async token =>
    {
        Console.WriteLine("Executing...");

        await Task.Delay(TimeSpan.FromSeconds(2), token);
        throw new InvalidOperationException("Something went wrong");
    }, CancellationToken.None);
}

async Task CompositeStrategySampleAsync()
{
    var pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>(),
            OnRetry = args =>
            {
                Console.WriteLine($"Retrying... {args.AttemptNumber + 1} attempt after {args.RetryDelay}");
                return default;
            }
        })
        .AddTimeout(TimeSpan.FromSeconds(1))
        .Build();

    await pipeline.ExecuteAsync(async token =>
    {
        Console.WriteLine("Executing...");

        await Task.Delay(TimeSpan.FromSeconds(2), token);
        throw new InvalidOperationException("Something went wrong");
    }, CancellationToken.None);
}