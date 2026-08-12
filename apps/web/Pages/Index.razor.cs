using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Hypen.Web.Pages;

public partial class Index : ComponentBase
{
    [Inject] 
    protected HttpClient Http { get; set; } = default!;

    [Inject] 
    protected IJSRuntime JS { get; set; } = default!;

    protected List<SongModel>? songs;
    protected string ytUrl = "";
    protected string statusMsg = "";
    protected bool isError = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadLibrary();
    }

    protected async Task LoadLibrary()
    {
        try
        {
            SetStatus("Memuat library...");
            songs = await Http.GetFromJsonAsync<List<SongModel>>("/api/songs");
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal memuat library: {ex.Message}", true);
        }
    }

    protected async Task ConvertVideo()
    {
        if (string.IsNullOrWhiteSpace(ytUrl)) return;
        SetStatus("Memproses track...");
        try
        {
            var res = await Http.PostAsJsonAsync("/api/convert", new { YoutubeUrl = ytUrl });
            if (res.IsSuccessStatusCode)
            {
                SetStatus("Track berhasil ditambahkan!");
                ytUrl = "";
                await LoadLibrary();
            }
        }
        catch
        {
            SetStatus("Gagal mengonversi video.", true);
        }
    }

    protected void PlaySong(SongModel song)
    {
        // Logika pemutaran audio
    }

    protected async Task DeleteSingle(int id)
    {
        if (!await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini?")) return;
        var res = await Http.DeleteAsync($"/api/songs/{id}");
        if (res.IsSuccessStatusCode) await LoadLibrary();
    }

    private void SetStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
    }

    public class SongModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string AudioUrl { get; set; } = "";
        public bool IsSelected { get; set; }
    }
}
