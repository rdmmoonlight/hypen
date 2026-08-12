# ========================================================
# Build Stage: Menggunakan .NET 10.0 SDK
# ========================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Salin file project (.csproj) dan restore dependencies
COPY ["Hypen.csproj", "./"]
RUN dotnet restore "Hypen.csproj"

# Salin seluruh kode program dan lakukan Publish/Build
COPY . .
RUN dotnet publish "Hypen.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ========================================================
# Runtime Stage: Menggunakan ASP.NET Core 10.0 Runtime
# ========================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app

# Expose port standar web service
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Hypen.dll"]
