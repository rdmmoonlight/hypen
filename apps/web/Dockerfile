# 1. Stage SDK untuk Build Application (.NET 10.0)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file dan restore dependency dari apps/web
COPY ["apps/web/Hypen.csproj", "apps/web/"]
RUN dotnet restore "apps/web/Hypen.csproj"

# Copy seluruh kode sumber dan publish
COPY . .
WORKDIR "/src/apps/web"
RUN dotnet publish "Hypen.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Stage Runtime + Dependencies (.NET 10.0 + Python 3 + FFmpeg + yt-dlp)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install Python 3, FFmpeg, dan Curl di OS Linux Container
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    ffmpeg \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Install / Download Binary Executable yt-dlp terbaru
RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp

# Copy hasil build dotnet dari stage build
COPY --from=build /app/publish .

# Set Port untuk Render / Cloud Hosting
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Hypen.dll"]
