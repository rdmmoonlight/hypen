# ==========================================
# 1. Stage SDK untuk Build Application
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file dari root
COPY ["Hypen.Web.csproj", "./"]
RUN dotnet restore "Hypen.Web.csproj"

# Copy seluruh kode sumber dan publish
COPY . .
RUN dotnet publish "Hypen.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# 2. Stage Runtime + Dependencies (Node.js + FFmpeg + yt-dlp)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install Python 3, Node.js (sebagai JS Runtime untuk yt-dlp), FFmpeg, & Curl
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    nodejs \
    ffmpeg \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Download Executable yt-dlp biner terbaru & beri izin eksekusi (+x)
RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp

# Copy hasil publish dotnet dari stage build
COPY --from=build /app/publish .
COPY cookies.txt /app/cookies.txt

# Set Port & Environment untuk Render / Cloud Hosting
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Hypen.Web.dll"]
