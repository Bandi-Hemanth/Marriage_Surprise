using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);
const string AdminDeleteEmail = "bandihemanth2602@gmail.com";
var renderPort = Environment.GetEnvironmentVariable("PORT");
var configuredDatabase = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");

builder.Services.AddOpenApi();

if (!string.IsNullOrWhiteSpace(configuredDatabase))
{
    var postgresConnectionString = BuildPostgresConnectionString(configuredDatabase);
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(postgresConnectionString));
}
else
{
    var dbPath = builder.Environment.IsDevelopment()
        ? "wedding.db"
        : "/data/wedding.db";

    if (!builder.Environment.IsDevelopment())
    {
        Directory.CreateDirectory("/data");
    }

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));
}

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(renderPort))
{
    app.Urls.Add($"http://0.0.0.0:{renderPort}");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "LiveMessages" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "GuestName" text NOT NULL,
                "Message" text NOT NULL,
                "IsDeveloper" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "MemoryPhotos" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "GuestName" text NOT NULL,
                "Caption" text NOT NULL,
                "ImageBase64" text NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
    }
    else
    {
        db.Database.EnsureCreated();
    }
}

app.MapGet("/api/live/messages", async (AppDbContext db) =>
{
    var messages = await db.LiveMessages
        .AsNoTracking()
        .OrderBy(m => m.CreatedAtUtc)
        .Select(m => new LiveMessage(
            m.Id,
            m.GuestName,
            m.Message,
            m.IsDeveloper,
            m.CreatedAtUtc.ToString("o")))
        .ToListAsync();

    return Results.Ok(messages);
});

app.MapPost("/api/live/messages", async (AppDbContext db, MessageCreateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.GuestName) || string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Name and message are required." });
    }

    var entity = new LiveMessageEntity
    {
        GuestName = request.GuestName.Trim(),
        Message = request.Message.Trim(),
        IsDeveloper = request.IsDeveloper,
        CreatedAtUtc = DateTime.UtcNow
    };

    db.LiveMessages.Add(entity);
    await db.SaveChangesAsync();

    return Results.Ok(new LiveMessage(
        entity.Id,
        entity.GuestName,
        entity.Message,
        entity.IsDeveloper,
        entity.CreatedAtUtc.ToString("o")));
});

app.MapDelete("/api/live/messages/{id:guid}", async (Guid id, HttpRequest httpRequest, AppDbContext db) =>
{
    var request = await httpRequest.ReadFromJsonAsync<MessageDeleteRequest>();
    if (!string.Equals(request?.Email?.Trim(), AdminDeleteEmail, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var entity = await db.LiveMessages.FirstOrDefaultAsync(m => m.Id == id);
    if (entity is null)
    {
        return Results.NotFound(new { error = "Message not found." });
    }

    db.LiveMessages.Remove(entity);
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true });
});

app.MapDelete("/api/live/memories/{id:guid}", async (Guid id, HttpRequest httpRequest, AppDbContext db) =>
{
    var request = await httpRequest.ReadFromJsonAsync<MessageDeleteRequest>();

    if (!string.Equals(request?.Email?.Trim(), AdminDeleteEmail, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var entity = await db.MemoryPhotos.FirstOrDefaultAsync(m => m.Id == id);
    if (entity is null)
    {
        return Results.NotFound(new { error = "Memory not found." });
    }

    db.MemoryPhotos.Remove(entity);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true });
});

app.MapGet("/api/live/memories", async (AppDbContext db) =>
{
    var memories = await db.MemoryPhotos
        .AsNoTracking()
        .OrderByDescending(m => m.CreatedAtUtc)
        .Select(m => new MemoryPhoto(
            m.Id,
            m.GuestName,
            m.Caption,
            m.ImageBase64,
            m.CreatedAtUtc.ToString("o")))
        .ToListAsync();

    return Results.Ok(memories);
});

app.MapPost("/api/live/memories", async (HttpRequest request, AppDbContext db) =>
{
    var form = await request.ReadFormAsync();
    var image = form.Files["memoryImage"];
    var guestName = form["guestName"].ToString();
    var caption = form["caption"].ToString();

    if (image is null || image.Length == 0)
    {
        return Results.BadRequest(new { error = "Please upload an image." });
    }

    if (string.IsNullOrWhiteSpace(guestName))
    {
        return Results.BadRequest(new { error = "Guest name is required." });
    }

    var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
    var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
    if (!allowed.Contains(extension))
    {
        return Results.BadRequest(new { error = "Only JPG, PNG, and WEBP files are supported." });
    }

    if (image.Length > 5 * 1024 * 1024)
    {
        return Results.BadRequest(new { error = "Image must be 5 MB or smaller." });
    }

    await using var memoryStream = new MemoryStream();
    await image.CopyToAsync(memoryStream);
    var base64 = Convert.ToBase64String(memoryStream.ToArray());
    var contentType = string.IsNullOrWhiteSpace(image.ContentType)
        ? "image/png"
        : image.ContentType;
    var dataUrl = $"data:{contentType};base64,{base64}";

    var entity = new MemoryPhotoEntity
    {
        GuestName = guestName.Trim(),
        Caption = string.IsNullOrWhiteSpace(caption) ? "Wedding memory" : caption.Trim(),
        ImageBase64 = dataUrl,
        CreatedAtUtc = DateTime.UtcNow
    };

    db.MemoryPhotos.Add(entity);
    await db.SaveChangesAsync();

    return Results.Ok(new MemoryPhoto(
        entity.Id,
        entity.GuestName,
        entity.Caption,
        entity.ImageBase64,
        entity.CreatedAtUtc.ToString("o")));
});

app.Run();

static string BuildPostgresConnectionString(string configuredDatabase)
{
    if (!Uri.TryCreate(configuredDatabase, UriKind.Absolute, out var databaseUri) ||
        !(databaseUri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
          databaseUri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
    {
        return configuredDatabase;
    }

    var userInfo = databaseUri.UserInfo.Split(':', 2, StringSplitOptions.TrimEntries);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = databaseUri.Host,
        Port = databaseUri.IsDefaultPort ? 5432 : databaseUri.Port,
        Database = databaseUri.AbsolutePath.Trim('/'),
        Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
        SslMode = SslMode.Require
    };

    foreach (var segment in databaseUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = segment.Split('=', 2);
        var key = Uri.UnescapeDataString(parts[0]);
        var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }
    }

    return builder.ConnectionString;
}

record MessageCreateRequest(string GuestName, string Message, bool IsDeveloper);
record MessageDeleteRequest(string Email);
record LiveMessage(Guid Id, string GuestName, string Message, bool IsDeveloper, string CreatedAtUtc);
record MemoryPhoto(Guid Id, string GuestName, string Caption, string ImageBase64, string CreatedAtUtc);

sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LiveMessageEntity> LiveMessages => Set<LiveMessageEntity>();
    public DbSet<MemoryPhotoEntity> MemoryPhotos => Set<MemoryPhotoEntity>();
}

sealed class LiveMessageEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GuestName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsDeveloper { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

sealed class MemoryPhotoEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GuestName { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string ImageBase64 { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
