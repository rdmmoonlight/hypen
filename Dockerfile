# ========================================================
# Build Stage: Menggunakan .NET 10.0 SDK
# ========================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Salin file .csproj apapun yang ada di folder root dan restore
COPY *.csproj ./
RUN dotnet restore

# Salin seluruh kode program dan lakukan Publish/Build
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ========================================================
# Runtime Stage: Menggunakan ASP.NET Core 10.0 Runtime
# ========================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

# Catatan: Jika nama DLL hasil build adalah huruf kecil (hypen.dll), 
# ubah "Hypen.dll" menjadi "hypen.dll" di bawah ini.
ENTRYPOINT ["dotnet", "Hypen.dll"]
