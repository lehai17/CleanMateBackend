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
using Google.Apis.Auth;
using Npgsql.EntityFrameworkCore.PostgreSQL;
// (BCrypt)
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

// ===== EF Core SQL Server =====
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));


// ===== CORS (FE dev 5173/3000) =====
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
                return Task.CompletedTask;
            }
        };
    });



builder.Services.AddAuthorization();

var app = builder.Build();

// ===== Swagger UI (Dev) =====
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(); // /swagger
}

// ===== Middlewares =====
//app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("_myAllowSpecificOrigins");
Console.WriteLine("✅ CORS policy '_myAllowSpecificOrigins' applied successfully!");

app.UseAuthentication();
app.UseAuthorization();


// ===================== API =====================

// --- Cleaners: public list ---
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


// Root endpoint to verify deployment
app.MapGet("/", () => Results.Ok("✅ CleanMate API is running on Render!"));


// --- /api/auth/register ---
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

// --- /api/auth/login ---
app.MapPost("/api/auth/login", async (AppDbContext db, LoginDto dto) =>
{
    var email = dto.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user is null)
    {
        Console.WriteLine($"❌ Không tìm thấy user: {email}");
        return Results.Unauthorized();
    }

    // 🧩 Thêm các dòng debug ở đây
    Console.WriteLine($"Login attempt: email={dto.Email}, pass={dto.Password}");
    Console.WriteLine($"Hash in DB: {user.PasswordHash}");
    Console.WriteLine($"Verify result: {BCryptNet.Verify(dto.Password, user.PasswordHash)}");

    if (!BCryptNet.Verify(dto.Password, user.PasswordHash))
    {
        Console.WriteLine("❌ Mật khẩu không khớp!");
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

    return Results.Ok(new
    {
        token = jwt,
        user = new { user.Id, user.FullName, user.Email, user.Role }
    });
});


//public record GoogleLoginDto(string idToken);
app.MapPost("/api/auth/google", async (AppDbContext db, GoogleLoginDto dto) =>
{
    Console.WriteLine("IdToken nhận được (prefix): " + (dto.IdToken?.Substring(0, Math.Min(20, dto.IdToken.Length)) ?? "null"));

    if (string.IsNullOrWhiteSpace(dto.IdToken))
        return Results.BadRequest("Missing idToken");

    using var client = new HttpClient();
    // gọi Google để verify id_token
    var res = await client.GetStringAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={dto.IdToken}");
    var info = JsonDocument.Parse(res).RootElement;

    var email = info.GetProperty("email").GetString();
    var name = info.GetProperty("name").GetString();

    if (email is null)
        return Results.BadRequest("Invalid Google token");

    // Tìm hoặc tạo user
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user == null)
    {
        user = new User
        {
            FullName = name ?? "Google User",
            Email = email,
            PasswordHash = "", // không cần mật khẩu
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    // Phát hành JWT
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




//app.MapPost("/api/auth/google", async (AppDbContext db, GoogleLoginDto dto) =>
//{
//    // DEBUG: in ra vài ký tự đầu để xác nhận client đang gửi gì
//    Console.WriteLine("IdToken nhận được (prefix): " + (dto.IdToken?.Substring(0, Math.Min(20, dto.IdToken.Length)) ?? "null"));

//    if (string.IsNullOrWhiteSpace(dto.IdToken))
//        return Results.BadRequest("Missing idToken");

//    try
//    {
//        // CHÚ Ý: dùng chính webClientId bạn cấu hình ở FE
//        var validPayload = await GoogleJsonWebSignature.ValidateAsync(
//            dto.IdToken,
//            new GoogleJsonWebSignature.ValidationSettings
//            {
//                // phải trùng với client id bạn dùng ở FE (expoClientId/webClientId)
//                Audience = new[] {
//                    "322324177186-rio3tjbo9jv5vgb7cbre62mkt83tehkr.apps.googleusercontent.com"
//                }
//            });

//        // DEBUG: log payload
//        Console.WriteLine($"Google payload email={validPayload.Email}, name={validPayload.Name}, aud={validPayload.Audience}");

//        var email = validPayload.Email;
//        var name = validPayload.Name ?? $"{validPayload.GivenName} {validPayload.FamilyName}".Trim();

//        if (string.IsNullOrEmpty(email))
//            return Results.BadRequest("Invalid Google token: no email");

//        // Tìm hoặc tạo user
//        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
//        if (user == null)
//        {
//            user = new User
//            {
//                FullName = string.IsNullOrWhiteSpace(name) ? "Google User" : name,
//                Email = email,
//                PasswordHash = "",
//                Role = UserRole.Customer
//            };
//            db.Users.Add(user);
//            await db.SaveChangesAsync();
//        }

//        // Phát hành JWT
//        var claims = new[]
//        {
//            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
//            new Claim(ClaimTypes.Role, user.Role.ToString()),
//            new Claim(JwtRegisteredClaimNames.Email, user.Email),
//            new Claim("name", user.FullName)
//        };

//        var token = new JwtSecurityToken(
//            issuer: jwtIssuer,
//            audience: jwtAudience,
//            claims: claims,
//            expires: DateTime.UtcNow.AddDays(7),
//            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
//        );

//        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

//        return Results.Ok(new
//        {
//            token = jwt,
//            user = new { user.Id, user.FullName, user.Email, user.Role }
//        });
//    }
//    catch (InvalidJwtException ex)
//    {
//        // DEBUG: lỗi xác thực token, in chi tiết
//        Console.WriteLine("ValidateAsync failed: " + ex.Message);
//        return Results.BadRequest("Invalid Google token: " + ex.Message);
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine("Unhandled exception at /api/auth/google: " + ex);
//        return Results.StatusCode(500);
//    }
//});



// --- /api/me (test token) ---
app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (sub is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        id = sub,
        name = user.FindFirstValue("name"),
        email = user.FindFirstValue(JwtRegisteredClaimNames.Email),
        role = user.FindFirstValue(ClaimTypes.Role)
    });
}).RequireAuthorization();

app.MapPost("/api/bookings", async (AppDbContext db, ClaimsPrincipal user, Booking booking) =>
{
    Console.WriteLine("==== JWT Claims ====");
    foreach (var c in user.Claims)
    {
        Console.WriteLine($"{c.Type} = {c.Value}");
    }

    var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
             ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (sub == null) return Results.Unauthorized();

    booking.UserId = int.Parse(sub);
    booking.CreatedAt = DateTime.UtcNow;

    db.Bookings.Add(booking);
    await db.SaveChangesAsync();

    return Results.Ok(new { booking.Id, booking.StartTime, booking.Price });
}).RequireAuthorization();


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

// /api/cleaner/jobs
app.MapGet("/api/cleaner/jobs", async (AppDbContext db, ClaimsPrincipal user) =>
{
    Console.WriteLine("==== [DEBUG] /api/cleaner/jobs ====");

    // Log tất cả các claims có trong JWT
    foreach (var c in user.Claims)
    {
        Console.WriteLine($"Claim: {c.Type} = {c.Value}");
    }

    // Thử lấy sub (user id)
    var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
              ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (sub is null)
    {
        Console.WriteLine("❌ sub is null => Unauthorized (token không có claim 'sub' hoặc 'nameidentifier')");
        return Results.Unauthorized();
    }

    Console.WriteLine($"✅ Extracted cleanerUserId = {sub}");

    var cleanerUserId = int.Parse(sub);

    Console.WriteLine($"🧹 Đang truy vấn công việc của Cleaner UserId = {cleanerUserId}");

    var jobs = await db.Bookings
        .Where(b => b.CleanerId == cleanerUserId)
        .OrderByDescending(b => b.StartTime)
        .Select(b => new
        {
            b.Id,
            b.StartTime,
            b.Price,
            b.Address,
            b.PaymentMethod,
            b.DurationHours
        })
        .ToListAsync();

    Console.WriteLine($"📦 Số công việc tìm thấy: {jobs.Count}");

    return Results.Ok(jobs);
}).RequireAuthorization();





// PUT: cập nhật địa chỉ, ghi chú, phương thức thanh toán...
app.MapPut("/api/orders/{id}", async (int id, AppDbContext db, Booking input) =>
{
    var booking = await db.Bookings.FindAsync(id);
    if (booking == null) return Results.NotFound();

    booking.Address = input.Address;
    booking.Notes = input.Notes;
    booking.PaymentMethod = input.PaymentMethod;
    booking.Address = input.Address;
    booking.StartTime = input.StartTime;
    booking.DurationHours = input.DurationHours;
    booking.Price = input.Price;

    await db.SaveChangesAsync();
    return Results.Ok(booking);
});

// DELETE
app.MapDelete("/api/orders/{id}", async (int id, AppDbContext db) =>
{
    var booking = await db.Bookings.FindAsync(id);
    if (booking == null) return Results.NotFound();

    db.Bookings.Remove(booking);
    await db.SaveChangesAsync();
    return Results.NoContent();
});


// ===================== Seed DEV =====================
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.EnsureCreated();

    if (!db.Users.Any())
    {
        var u1 = new User { FullName = "Nguyen Van A", Email = "a@test.com", PasswordHash = BCryptNet.HashPassword("dev"), Role = UserRole.Cleaner };
        var u2 = new User { FullName = "Tran Thi B", Email = "b@test.com", PasswordHash = BCryptNet.HashPassword("dev"), Role = UserRole.Cleaner };
        db.Users.AddRange(u1, u2);
        db.SaveChanges();

        db.CleanerProfiles.AddRange(
            new CleanerProfile
            {
                UserId = u1.Id,
                Bio = "Chuyên dọn nhà",
                HourlyRate = 120000m,
                AvgRating = 4.6,
                City = "Hà Nội",
                AddressText = "Hồ Tân Xã",
                Latitude = 21.0278,
                Longitude = 105.8342
            },
            new CleanerProfile
            {
                UserId = u2.Id,
                Bio = "Chuyên giặt ủi",
                HourlyRate = 100000m,
                AvgRating = 4.4,
                City = "Hà Nội",
                AddressText = "Thôn 2",
                Latitude = 10.7769,
                Longitude = 106.7009
            }
        );
        db.SaveChanges();
    }
}

// ===== Auto apply migrations on startup =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate(); // Tự động apply tất cả migrations
    Console.WriteLine("✅ Database migrated successfully!");
}


app.Run();
