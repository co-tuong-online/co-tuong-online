using Microsoft.OpenApi.Models;

namespace CoTuongOnline.Infrastructure;

public static class SwaggerSetup
{
    /// <summary>Swagger UI tại /swagger để Tester thử API (có nút Authorize nhập JWT).</summary>
    public static IServiceCollection AddApiDocs(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Cờ Tướng Online API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Dán accessToken nhận được từ /api/v1/auth/login"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });
        });
        return services;
    }

    public static WebApplication UseApiDocs(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.DocumentTitle = "Cờ Tướng Online API");
        return app;
    }
}
