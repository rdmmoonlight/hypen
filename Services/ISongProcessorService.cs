namespace Hypen.Web.Services;

public interface ISongProcessorService
{
    Task<int> ProcessPendingSongsAsync();
}
