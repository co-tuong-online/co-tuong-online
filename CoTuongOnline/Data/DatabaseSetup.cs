using CoTuongOnline.Repositories;
using CoTuongOnline.Repositories.InMemory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoTuongOnline.Data;

public static class DatabaseSetup
{
    public static bool UsesSqlServer(IConfiguration config) =>
        !string.Equals(config["Database:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Chọn nơi lưu dữ liệu theo "Database:Provider":
    ///   SqlServer (mặc định) — dùng chuỗi kết nối ConnectionStrings:Default.
    ///   InMemory — lưu RAM, dùng khi máy chưa cài SQL Server.
    /// </summary>
    public static IServiceCollection AddAppDatabase(this IServiceCollection services, IConfiguration config)
    {
        if (!UsesSqlServer(config))
        {
            services.AddSingleton<InMemoryDataStore>();
            services.AddScoped<IUserRepository, InMemoryUserRepository>();
            services.AddScoped<IOtpRepository, InMemoryOtpRepository>();
            services.AddScoped<IGameRepository, InMemoryGameRepository>();
            services.AddScoped<ISavedGameRepository, InMemorySavedGameRepository>();
            return services;
        }

        string conn = config.GetConnectionString("Default")
                      ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Default trong appsettings.json.");
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(conn));
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IOtpRepository, EfOtpRepository>();
        services.AddScoped<IGameRepository, EfGameRepository>();
        services.AddScoped<ISavedGameRepository, EfSavedGameRepository>();
        return services;
    }

    /// <summary>
    /// Khi khởi động: tự tạo database và áp dụng các migration còn thiếu.
    /// Nhờ vậy người clone dự án về chỉ cần "dotnet run", không phải chạy lệnh SQL nào.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        if (!UsesSqlServer(app.Configuration))
        {
            app.Logger.LogWarning("Đang lưu dữ liệu trong RAM (Database:Provider = InMemory). Tắt server là mất dữ liệu.");
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            if (db.Database.GetMigrations().Any())
            {
                await db.Database.MigrateAsync();
                app.Logger.LogInformation("Database sẵn sàng: {Db}", db.Database.GetDbConnection().Database);
            }
            else
            {
                // Chưa có thư mục Migrations (chưa chạy "dotnet ef migrations add") → tạo bảng trực tiếp
                await db.Database.EnsureCreatedAsync();
                app.Logger.LogWarning("Chưa có migration nào, đã tạo bảng bằng EnsureCreated. Nên chạy: dotnet ef migrations add InitialCreate");
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(ex,
                "Không kết nối được SQL Server. Kiểm tra ConnectionStrings:Default trong appsettings.json " +
                "(LocalDB: (localdb)\\MSSQLLocalDB, SQL Express: .\\SQLEXPRESS), " +
                "hoặc tạm đặt \"Database:Provider\": \"InMemory\" để chạy không cần SQL Server.");
            throw;
        }
    }
}

/// <summary>
/// Dùng cho lệnh "dotnet ef migrations add ..." — tạo DbContext trực tiếp từ appsettings
/// mà không cần khởi động toàn bộ web server (không cần JWT key, email...).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(config.GetConnectionString("Default"))
            .Options;
        return new AppDbContext(options);
    }
}
