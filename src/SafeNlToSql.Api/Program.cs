using System.Text.Json;
using SafeNlToSql.Api;
using SafeNlToSql.Application;
using SafeNlToSql.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<SqlGuard>();
builder.Services.AddSingleton<ISqlGenerator, NotConfiguredSqlGenerator>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddProblemDetails();

// Rows are dictionaries: PropertyNamingPolicy (camelCase by default) does not apply to their keys.
builder.Services.ConfigureHttpJsonOptions(
    options => options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

// In every environment, an unexpected exception becomes a generic 500 with no exception text.
app.UseExceptionHandler();

app.MapSearch();

app.Run();
