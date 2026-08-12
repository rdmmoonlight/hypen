using Microsoft.AspNetCore.Mvc;
using Npgsql;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Amazon.S3;
using Amazon.S3.Transfer;

var builder = WebApplication.CreateBuilder(args);

// CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors("AllowAll");

// Environment Variables
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION")!;
string r2ServiceUrl = Environment.GetEnvironmentVariable("R2_SERVICE_URL")!;
string r2AccessKey = Environment.GetEnvironmentVariable("R2_ACCESS_KEY")!;
string r2SecretKey = Environment.GetEnvironmentVariable("R2_SECRET_KEY")!;
string r2BucketName = Environment.GetEnvironmentVariable("R2_BUCKET_NAME")!;
string r2PublicDomain = Environment.GetEnvironmentVariable("R2_PUBLIC_DOMAIN")!;

app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
{
    var youtube = new YoutubeClient();
    var video = await youtube.Videos.GetAsync(req.YoutubeUrl);
    var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);

    // 1. Stream audio terbaik
    var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
    using var audioStream = await youtube.Videos.Streams.GetAsync(streamInfo);

    // 2. Upload ke Cloudflare R2
    string fileName = $"{video.Id}.mp3";
    var s3Config = new AmazonS3Config { ServiceURL = r2ServiceUrl };
    using var s3Client = new AmazonS3Client(r2AccessKey, r2SecretKey, s3Config);

    var fileTransferUtility = new TransferUtility(s3Client);
    await fileTransferUtility.UploadAsync(audioStream, r2BucketName, fileName);

    string audioPublicUrl = $"{r2PublicDomain}/{fileName}";

    // Get Thumbnail URL Resolusi Tertinggi via LINQ
    string coverUrl = video.Thumbnails
        .OrderByDescending(t => t.Resolution.Area)
        .FirstOrDefault()?.Url ?? "";

    // 3. Simpan ke Neon PostgreSQL
    using var conn = new NpgsqlConnection(dbConnectionString);
    await conn.OpenAsync();

    string sql = @"INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds) 
                   VALUES (@yid, @title, @artist, @cover, @url, @dur) 
                   RETURNING id;";

    using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("yid", video.Id.Value);
    cmd.Parameters.AddWithValue("title", video.Title);
    cmd.Parameters.AddWithValue("artist", video.Author.ChannelTitle);
    cmd.Parameters.AddWithValue("cover", coverUrl);
    cmd.Parameters.AddWithValue("url", audioPublicUrl);
    cmd.Parameters.AddWithValue("dur", (int)(video.Duration?.TotalSeconds ?? 0));

    var songId = await cmd.ExecuteScalarAsync();

    return Results.Ok(new { Id = songId, video.Title, AudioUrl = audioPublicUrl });
});

app.MapGet("/api/songs", async () =>
{
    var songs = new List<object>();
    using var conn = new NpgsqlConnection(dbConnectionString);
    await conn.OpenAsync();

    using var cmd = new NpgsqlCommand("SELECT id, title, artist, cover_url, audio_url FROM songs ORDER BY id DESC", conn);
    using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        songs.Add(new
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            Artist = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
            Cover = reader.IsDBNull(3) ? "" : reader.GetString(3),
            AudioUrl = reader.GetString(4)
        });
    }

    return Results.Ok(songs);
});

app.Run();

public record ConvertRequest(string YoutubeUrl);
