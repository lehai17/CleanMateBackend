# Dùng .NET 8 SDK để build ứng dụng
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Sao chép file project và khôi phục dependencies
COPY CleanMate.Api/*.csproj ./CleanMate.Api/
RUN dotnet restore CleanMate.Api/CleanMate.Api.csproj

# Sao chép toàn bộ code và build ra thư mục out
COPY . .
RUN dotnet publish CleanMate.Api/CleanMate.Api.csproj -c Release -o out

# Dùng runtime nhỏ gọn để chạy app
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

# Cổng ứng dụng (Render tự map)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "CleanMate.Api.dll"]
