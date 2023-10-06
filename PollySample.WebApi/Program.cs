using System.Net;
using Microsoft.Net.Http.Headers;
using Polly;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddResiliencePipeline("timeout", (builder, context) =>
{
    _ = builder.AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(2),
        OnTimeout = args =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Timeout occurred after: {TotalSeconds} seconds", args.Timeout.TotalSeconds);
            return default;
        }
    });
});

builder.Services.AddResiliencePipeline<string, HttpResponseMessage>("http", (builder, context) =>
{
    _ = builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => r.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError),
        DelayGenerator = args =>
        {
            if (args.Outcome.Result is not null && args.Outcome.Result.Headers.TryGetValues(HeaderNames.RetryAfter, out var value))
            {
                return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(int.Parse(value.First())));
            }

            return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber + 1)));
        },
        OnRetry = args =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Retrying... {AttemptNumber} attempt after {RetryDelay}", args.AttemptNumber + 1, args.RetryDelay);
            return default;
        }
    });
});

builder.Services.AddTransient<TransientErrorDelegatingHandler>();
builder.Services.AddHttpClient("http")
    .AddHttpMessageHandler<TransientErrorDelegatingHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    _ = app.UseSwagger();
    _ = app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", async (ResiliencePipelineProvider<string> pipelineProvider) =>
{
    var pipeline = pipelineProvider.GetPipeline("timeout");

    var forecast = await pipeline.ExecuteAsync(async (token) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(4), token);

        var forecast = Enumerable.Range(1, 5).Select(index =>
               new WeatherForecast
               (
                   DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                   Random.Shared.Next(-20, 55),
                   summaries[Random.Shared.Next(summaries.Length)]
               ))
               .ToArray();

        return forecast;
    });

    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.MapGet("/api/http/{statusCode:int}", async (int statusCode, IHttpClientFactory httpClientFactory) =>
{
    var httpClient = httpClientFactory.CreateClient("http");
    var response = await httpClient.GetAsync($"https://httpstat.us/{statusCode}");

    return response.StatusCode;
});

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public class TransientErrorDelegatingHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> pipeline;

    public TransientErrorDelegatingHandler(ResiliencePipelineProvider<string> pipelineProvider)
    {
        pipeline = pipelineProvider.GetPipeline<HttpResponseMessage>("http");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await pipeline.ExecuteAsync(async _ => await base.SendAsync(request, cancellationToken), cancellationToken);
}