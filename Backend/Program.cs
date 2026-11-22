using Backend.Data;
using Backend.Endpoints;
using Backend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// 1. Строка подключения
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("AR_INVENTORY_DB_CONNECTION");
    
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Database connection is not configured. Set ConnectionStrings:DefaultConnection in a local appsettings.Development.json or AR_INVENTORY_DB_CONNECTION in the environment.");

// 2. Регистрация NpgsqlDataSource (единый пул соединений, Singleton)
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

// 3. Регистрация репозиториев и сервисов (Scoped)
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<QrRepository>();
builder.Services.AddScoped<SessionRepository>();
builder.Services.AddScoped<QrDeletionRepository>();
builder.Services.AddScoped<BoxRepository>();
builder.Services.AddScoped<InventoryRunRepository>();
builder.Services.AddScoped<InventorySyncService>();

// 4. Swagger и CORS
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.WebHost.UseUrls("http://0.0.0.0:8000");

var app = builder.Build();

app.UseCors("AllowAll");

// 5. Статика из папки wwwroot (admin.html, main.js, style.css)
app.UseStaticFiles();

// 6. Swagger UI
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AR Inventory API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "AR Inventory API -- Документация";
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    options.DefaultModelsExpandDepth(2);
    options.DefaultModelExpandDepth(2);
    options.DisplayRequestDuration();
    options.EnableTryItOutByDefault();
    options.HeadContent = @"
        <style>
            .swagger-ui .topbar { background-color: #1a1d23; }
            .swagger-ui .topbar .download-url-wrapper .select-label span { color: #ffffff; }
            .swagger-ui .info .title { color: #2563eb; }
        </style>";
});

// 7. Регистрация всех групп эндпоинтов
app.MapSystemEndpoints();
app.MapPublicEndpoints();
app.MapUserAdminEndpoints();
app.MapQrAdminEndpoints();
app.MapSessionAdminEndpoints();
app.MapInventoryEndpoints();
app.MapAdminInventoryEndpoints();

app.Run();
