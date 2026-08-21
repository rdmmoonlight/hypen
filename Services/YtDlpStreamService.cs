using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Hypen.Web.Services;

public class YtDlpStreamService
{
    private readonly HttpClient _httpClient;
    private readonly string _dockerApiBaseUrl;

    public YtDlpStreamService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        // Alamat Docker API Wrapper (sesuaikan port / host jika beda container)
        _dockerApiBaseUrl = "http://localhost:8080"; 
    }

    public async IAsyncEnumerable<string> StreamDownloadAsync(
        string youtubeUrlOrId, 
        string outputDirectory, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return "[INFO] Mengirim permintaan ke Docker yt-dlp API Service...";

        // 1. Cek kesehatan service Docker API
        bool isApiHealthy = await CheckDockerApiHealthAsync(cancellationToken);
        if (!isApiHealthy)
        {
            yield return "[ERROR] Docker yt-dlp API Service tidak dapat dihubungi di " + _dockerApiBaseUrl;
            yield break;
        }

        // 2. Siapkan Request Body untuk Docker API Wrapper
        var payload = new
        {
            url = youtubeUrlOrId,
            format = "mp3",
            quality = "best",
            outDir = outputDirectory
        };

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{_dockerApiBaseUrl}/download")
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage? response = null;
        try
        {
            // Kirim request dan baca respon secara streaming (ResponseHeadersRead)
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            yield return $"[ERROR] Koneksi ke Docker API gagal: {ex.Message}";
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            yield return $"[ERROR] Docker API merespons dengan HTTP Status: {response.StatusCode}";
            yield break;
        }

        // 3. Stream output dari Docker API ke C# AsyncEnumerable
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return "[CANCELLED] Pemrosesan dibatalkan oleh pengguna.";
                yield break;
            }

            string? line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }

        yield return "[COMPLETED] Pemrosesan melalui Docker API selesai!";
    }

    private async Task<bool> CheckDockerApiHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var res = await _httpClient.GetAsync($"{_dockerApiBaseUrl}/ping", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
