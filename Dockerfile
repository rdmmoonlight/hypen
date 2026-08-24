# ==========================================
# 1. Stage SDK untuk Build Application
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy seluruh file project (.csproj) beserta dependensi lokalnya
COPY ["apps/web/Hypen.Web.csproj", "apps/web/"]
COPY ["src/JUUI/JUUI.csproj", "src/JUUI/"]

# Restore dependensi dengan parameter verbose jika terjadi kelambatan jaringan
RUN dotnet restore "apps/web/Hypen.Web.csproj" --disable-parallel

# Copy seluruh source code repositori
COPY . .

# Publish aplikasi ke folder /app/publish
WORKDIR "/src/apps/web"
RUN dotnet publish "Hypen.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# 2. Stage Runtime Murni ASP.NET Core (.NET 10)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install ca-certificates & curl untuk healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy hasil publish dari stage build
COPY --from=build /app/publish .

# Set Port & Environment untuk Render / Cloud Hosting
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Hypen.Web.dll"]
