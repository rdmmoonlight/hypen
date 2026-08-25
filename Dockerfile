# ==========================================
# 1. Stage SDK untuk Build Application
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file dan restore (optimasi cache layer)
COPY ["Hypen.Web.csproj", "./"]
RUN dotnet restore "Hypen.Web.csproj"

# Copy seluruh kode sumber dan publish
COPY . .
RUN dotnet publish "Hypen.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# 2. Stage Runtime Murni ASP.NET Core (.NET 10)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install ca-certificates & curl untuk healthcheck / kebutuhan HTTPS standar
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Set Port & Environment untuk Render / Cloud Hosting
# Standar baru .NET 8+ menggunakan ASPNETCORE_HTTP_PORTS
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Copy hasil publish dari stage build beserta hak kepemilikan ke user 'app'
COPY --from=build --chown=app:app /app/publish .

# BEST PRACTICE: Gunakan user non-root bawaan image .NET demi keamanan
USER app

ENTRYPOINT ["dotnet", "Hypen.Web.dll"]
