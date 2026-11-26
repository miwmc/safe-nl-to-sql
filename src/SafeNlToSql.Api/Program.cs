using System.Text.Json;
using SafeNlToSql.Api;
using SafeNlToSql.Application;
using SafeNlToSql.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<SqlGuard>();

// The presence of the AzureOpenAI section turns the Azure generator on.
builder.Services.AddSqlGenerator(_ => new NotConfiguredSqlGenerator());

builder.Services.AddSingleton<SearchService>();
builder.Services.AddProblemDetails();

// Rows are dictionaries: PropertyNamingPolicy (camelCase by default) does not apply to their keys.
builder.Services.ConfigureHttpJsonOptions(
    options => options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

// Created now, not on the first request, so that a partial AzureOpenAI section fails startup.
app.Services.GetRequiredService<ISqlGenerator>();

// In every environment, an unexpected exception becomes a generic 500 with no exception text.
// BadHttpRequestException keeps its own status: in Development, a body that cannot be bound is thrown as one.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});

app.MapSearch();

app.Run();
