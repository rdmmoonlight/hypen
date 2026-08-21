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
# 2. Stage Runtime + Dependencies (Node.js + Deno + FFmpeg + yt-dlp)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install Python 3, Node.js, FFmpeg, Curl, Unzip & Ca-certificates
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    nodejs \
    ffmpeg \
    curl \
    ca-certificates \
    unzip \
    && rm -rf /var/lib/apt/lists/*

# Install Deno (JS Runtime optimal untuk EJS/Phantom solver yt-dlp)
RUN curl -fsSL https://deno.land/install.sh | sh \
    && mv /root/.deno/bin/deno /usr/local/bin/deno \
    && chmod a+rx /usr/local/bin/deno

# Download Executable yt-dlp biner terbaru, beri izin eksekusi, dan perbarui
RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && yt-dlp -U

# Copy hasil publish dotnet dari stage build
COPY --from=build /app/publish .

# ==========================================
# 3. PENANGANAN COOKIES (BYPASS BOT DI RENDER)
# ==========================================
# Menggunakan wildcard (*) agar build tidak error jika cookies.txt belum di-commit
COPY cookies.txt* /app/
RUN if [ -f /app/cookies.txt ]; then chmod 644 /app/cookies.txt; fi

# Set Port & Environment untuk Render / Cloud Hosting
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Hypen.Web.dll"]
