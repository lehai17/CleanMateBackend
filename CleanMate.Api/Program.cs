using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BCryptNet = BCrypt.Net.BCrypt;

var builder = WebApplication.CreateBuilder(args);

// ===== Swagger =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ===== Database Configuration (auto switch PostgreSQL / SQL Server) =====
var connectionString = builder.Configuration.GetConnectionString("Default");

if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
{
    // ✅ PostgreSQL for Render
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString,
            npgsql => npgsql.MigrationsAssembly("CleanMate.Api")
                             .MigrationsHistoryTable("__EFMigrationsHistory", "public")));
    Console.WriteLine("🟢 Using PostgreSQL (Render)");
}
else
{
    // ✅ SQL Server for local dev
    builder.Services.AddDbContext<AppDbContextSqlServer>(options =>
        options.UseSqlServer(connectionString,
            sql => sql.MigrationsAssembly("CleanMate.Api")
                      .MigrationsHistoryTable("__EFMigrationsHistory", "dbo")));

    // Alias cho AppDbContext
    builder.Services.AddScoped<AppDbContext, AppDbContextSqlServer>();
    Console.WriteLine("🟡 Using SQL Server (Local)");
}

Console.WriteLine($"🌐 Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"🔌 Connection: {connectionString}");

// ===== CORS =====
var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins, policy =>
    {
        policy.WithOrigins(
            "http://localhost:5238",
            "http://localhost:3000",
            "https://cleanmate-web.onrender.com"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

// ===== JWT Auth =====
var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            IssuerSigningKey = signingKey
        };

        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                Console.WriteLine("Auth header: " + ctx.Request.Headers["Authorization"].ToString());
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = ctx =>
            {
                Console.WriteLine("JWT failed: " + ctx.Exception.Message);

                Console.WriteLine("Failed Authorization Header: " + ctx.Request.Headers["Authorization"].ToString());
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ✅ Add this to support controllers (Swagger + future MVC)
builder.Services.AddControllers();

var app = builder.Build();

// ===== Swagger UI =====
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ===== Middleware order =====
app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);
Console.WriteLine("✅ CORS policy '_myAllowSpecificOrigins' applied successfully!");

app.UseAuthentication();
app.UseAuthorization();

// ===== Map controllers (safe now) =====
app.MapControllers();

// ===================== API Endpoints =====================

// --- Cleaners ---
app.MapGet("/api/cleaners", async (AppDbContext db) =>
{
    var list = await db.CleanerProfiles
        .Include(c => c.User)
        .Select(c => new
        {
            c.Id,
            Name = c.User!.FullName,
            c.City,
            c.HourlyRate,
            c.AvgRating,
            c.Bio,
            c.AddressText
        })
        .ToListAsync();

    return Results.Ok(list);
});

// Root endpoint
app.MapGet("/", () => Results.Ok("✅ CleanMate API is running!"));

// --- Register ---
app.MapPost("/api/auth/register", async (AppDbContext db, RegisterDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest("Email/Password required");

    var email = dto.Email.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(u => u.Email == email))
        return Results.BadRequest("Email already used");

    var user = new User
    {
        FullName = dto.FullName,
        Email = email,
        PasswordHash = BCryptNet.HashPassword(dto.Password),
        Role = dto.Role
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    if (dto.Role == UserRole.Cleaner)
    {
        db.CleanerProfiles.Add(new CleanerProfile
        {
            UserId = user.Id,
            Bio = "New cleaner",
            HourlyRate = 120000m,
            City = "Hà Nội",
            AddressText = ""
        });
        await db.SaveChangesAsync();
    }

    return Results.Ok(new { user.Id, user.FullName, user.Email, user.Role });
});

// --- Login ---
app.MapPost("/api/auth/login", async (AppDbContext db, LoginDto dto) =>
{
    var email = dto.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user is null)
    {
        Console.WriteLine($"Login failed: User with email {email} not found.");
        return Results.Unauthorized();
    }

    if (!BCryptNet.Verify(dto.Password, user.PasswordHash))
    {
        Console.WriteLine($"Login failed: Incorrect password for user {email}.");
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim("name", user.FullName)
    };

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
    );

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    Console.WriteLine($"User {email} successfully logged in.");

    return Results.Ok(new
    {
        token = jwt,
        user = new { user.Id, user.FullName, user.Email, user.Role }
    });
});

// --- Google Login ---
app.MapPost("/api/auth/google", async (AppDbContext db, GoogleLoginDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.IdToken))
        return Results.BadRequest("Missing idToken");

    using var client = new HttpClient();
    var res = await client.GetStringAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={dto.IdToken}");
    var info = JsonDocument.Parse(res).RootElement;

    var email = info.GetProperty("email").GetString();
    var name = info.GetProperty("name").GetString();

    if (email is null)
        return Results.BadRequest("Invalid Google token");

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user == null)
    {
        user = new User
        {
            FullName = name ?? "Google User",
            Email = email,
            PasswordHash = "",
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim("name", user.FullName)
    };

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
    );

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new
    {
        token = jwt,
        user = new { user.Id, user.FullName, user.Email, user.Role }
    });
});

// --- Me ---
app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
              ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (sub is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        id = sub,
        name = user.FindFirstValue("name"),
        email = user.FindFirstValue(JwtRegisteredClaimNames.Email),
        role = user.FindFirstValue(ClaimTypes.Role)
    });
}).RequireAuthorization();

// --- Bookings ---
app.MapPost("/api/bookings", async (AppDbContext db, ClaimsPrincipal user, Booking booking) =>
{
    var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
             ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (sub == null) return Results.Unauthorized();

    booking.UserId = int.Parse(sub);
    booking.CreatedAt = DateTime.UtcNow;

    db.Bookings.Add(booking);
    await db.SaveChangesAsync();

    return Results.Ok(new { booking.Id, booking.StartTime, booking.Price });
}).RequireAuthorization();

app.MapPut("/api/bookings/{id}/accept", async (IServiceProvider sp, int id, int cleanerId) =>
{
    var db = sp.GetRequiredService<AppDbContext>();
    Console.WriteLine($"📩 Accept booking called: id={id}, cleaner={cleanerId}");

    var booking = await db.Bookings.FindAsync(id);
    if (booking == null)
    {
        Console.WriteLine($"❌ Booking {id} not found in database");
        return Results.NotFound(new { message = $"Booking {id} not found." });
    }

    if (booking.Status != "Pending")
        return Results.BadRequest(new { message = "Booking is already accepted or completed." });

    booking.CleanerId = cleanerId;
    booking.Status = "Accepted";
    await db.SaveChangesAsync();

    Console.WriteLine($"✅ Cleaner {cleanerId} accepted booking {id}");
    return Results.Ok(new { message = "Booking accepted successfully!" });
});


// --- Update Cleaner Profile ---
app.MapPut("/api/cleaner/profile", async (AppDbContext db, ClaimsPrincipal user, CleanerProfileUpdateDto dto) =>
{
    var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (sub == null) return Results.Unauthorized();

    var userId = int.Parse(sub);
    var cleanerProfile = await db.CleanerProfiles.Include(c => c.User)
        .FirstOrDefaultAsync(c => c.UserId == userId);

    if (cleanerProfile == null)
        return Results.NotFound(new { message = "Cleaner profile not found." });

    // Update basic fields
    cleanerProfile.User.FullName = dto.FullName;
    cleanerProfile.User.PhoneNumber = dto.PhoneNumber;
    cleanerProfile.AddressText = dto.AddressText;
    cleanerProfile.Bio = dto.Bio;

    await db.SaveChangesAsync();

    Console.WriteLine($"🧹 Cleaner {userId} updated profile at {DateTime.Now:HH:mm:ss}");
    return Results.Ok(new
    {
        message = "Profile updated successfully!",
        cleanerProfile.User.FullName,
        cleanerProfile.User.PhoneNumber,
        cleanerProfile.AddressText,
        cleanerProfile.Bio
    });
}).RequireAuthorization();



// --- Orders ---
app.MapGet("/api/orders", async (AppDbContext db, int userId) =>
{
    var list = await db.Bookings
        .Where(b => b.UserId == userId)
        .OrderByDescending(b => b.CreatedAt)
        .Select(b => new
        {
            id = b.Id,
            orderCode = "CM" + b.Id,
            date = b.StartTime,
            price = b.Price,
            address = b.Address,
            paymentMethod = b.PaymentMethod,
            createdAt = b.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(list);
});

// --- Delete Order ---
// --- Delete Order ---
app.MapDelete("/api/orders/{id}", async (AppDbContext db, int id) =>
{
    Console.WriteLine($"🟡 [DELETE] Request received for Order ID = {id}");

    try
    {
        // Kiểm tra tồn tại
        var booking = await db.Bookings.FindAsync(id);
        if (booking == null)
        {
            Console.WriteLine($"❌ [DELETE] Booking with ID {id} not found.");
            return Results.NotFound(new { message = $"Booking ID {id} not found" });
        }

        // Xóa bản ghi
        db.Bookings.Remove(booking);
        await db.SaveChangesAsync();

        Console.WriteLine($"✅ [DELETE] Booking ID {id} deleted successfully at {DateTime.Now:HH:mm:ss}");
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"🔥 [ERROR] Exception while deleting booking ID {id}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        return Results.Problem($"Internal server error while deleting booking {id}: {ex.Message}");
    }
}).RequireAuthorization();




// --- MoMo Payment ---
// POST /api/payment/momo
app.MapPost("/api/payment/momo", async (HttpContext context) =>
{
    try
    {
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var json = JsonDocument.Parse(body);
        var amount = json.RootElement.GetProperty("amount").GetDecimal();
        var orderInfo = json.RootElement.GetProperty("orderInfo").GetString() ?? "Thanh toán dịch vụ CleanMate";

        // 🧩 Thông tin MoMo sandbox
        const string endpoint = "https://test-payment.momo.vn/v2/gateway/api/create";
        const string partnerCode = "MOMOXXXX2024";  // 👉 thay bằng PartnerCode của bạn
        const string accessKey = "your_access_key"; // 👉 thay bằng AccessKey
        const string secretKey = "your_secret_key"; // 👉 thay bằng SecretKey

        var orderId = DateTime.Now.Ticks.ToString();
        var requestId = Guid.NewGuid().ToString();
        var redirectUrl = "http://localhost:3000/payment-success";
        var ipnUrl = "http://localhost:5238/api/payment/momo-callback";

        // Chuẩn bị chuỗi ký
        var rawSignature =
            $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType=captureWallet";

        // Ký SHA256
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var signature = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSignature))).Replace("-", "").ToLower();

        // Gửi body tới MoMo
        var requestBody = new
        {
            partnerCode,
            partnerName = "CleanMate",
            storeId = "CleanMateStore",
            requestId,
            amount,
            orderId,
            orderInfo,
            redirectUrl,
            ipnUrl,
            lang = "vi",
            requestType = "captureWallet",
            signature
        };

        using var client = new HttpClient();
        var res = await client.PostAsync(endpoint,
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

        var responseContent = await res.Content.ReadAsStringAsync();
        Console.WriteLine("💰 MoMo response: " + responseContent);
        return Results.Content(responseContent, "application/json");
    }
    catch (Exception ex)
    {
        Console.WriteLine("🔥 MoMo Payment Error: " + ex.Message);
        return Results.Problem("Internal Server Error: " + ex.Message);
    }
});

// MoMo callback (notify thanh toán thành công)
app.MapPost("/api/payment/momo-callback", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    Console.WriteLine("📩 [MoMo Callback] " + body);

    return Results.Ok(new { message = "Callback received" });
});




// ===================== Auto Migration =====================
using (var scope = app.Services.CreateScope())
{
    var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    try
    {
        if (env.IsProduction())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // PostgreSQL
            db.Database.Migrate();
            Console.WriteLine("✅ PostgreSQL migrated successfully (Render)!"); // Log migration thành công
        }
        else
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContextSqlServer>(); // SQL Server Local
            db.Database.Migrate();
            Console.WriteLine("✅ SQL Server migrated successfully (Local)!"); // Log migration thành công
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error while connecting to database: " + ex.Message); // Log lỗi nếu gặp sự cố kết nối
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
        Console.WriteLine("✅ PostgreSQL database migrated successfully on Render!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Migration failed: {ex.Message}");
    }
}



app.Run();
