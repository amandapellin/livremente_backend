using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<LivreMenteDbContext>(opt =>
    opt.UseNpgsql(connectionString, o =>
    {
        o.MapEnum<PublicationSource>("source_enum");
        o.MapEnum<PublicationType>("type_enum");
        o.MapEnum<ReadingStatus>("reading_status_enum");
        o.MapEnum<PreferenceType>("preference_type_enum");
    }));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors("FrontendPolicy");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.Run();

