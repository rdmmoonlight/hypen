using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace Hypen.Web.Services;

public class YtDlpStreamService
{
    private readonly HttpClient _httpClient;
    // URL Internal Render.com atau Host Worker API Docker
    private readonly string _workerApiUrl = "http://yt-worker-service:5000/api/download/audio"; 

    public YtDlpStreamService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async IAsyncEnumerable<string> StreamDownloadAsync(
        string youtubeUrl, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = new { url = youtubeUrl };

        using var request = new HttpRequestMessage(HttpMethod.Post, _workerApiUrl)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            yield return $"[ERROR] Worker API merespons status: {response.StatusCode}";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            string? line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }
}
