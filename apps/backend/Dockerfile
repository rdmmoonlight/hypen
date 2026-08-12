# 1. Stage Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file dari path monorepo
COPY ["apps/backend/Hypen.csproj", "apps/backend/"]
RUN dotnet restore "apps/backend/Hypen.csproj"

# Copy seluruh source code
COPY . .
WORKDIR "/src/apps/backend"
RUN dotnet publish "Hypen.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Stage Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Port default Render
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Hypen.dll"]
