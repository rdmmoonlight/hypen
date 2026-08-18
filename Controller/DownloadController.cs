using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.IO.Compression;

namespace Hypen.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DownloadController : ControllerBase
    {
        private readonly string _downloadFolder;

        public DownloadController(IWebHostEnvironment env)
        {
            _downloadFolder = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "downloads");
        }

        [HttpPost("download-to-client")]
        public IActionResult DownloadFilesToUser([FromBody] List<string> relativePaths)
        {
            if (relativePaths == null || relativePaths.Count == 0)
                return BadRequest("Tidak ada file yang dipilih.");

            var validFiles = new List<(string FullPath, string ZipEntryName)>();

            foreach (var relPath in relativePaths)
            {
                // Mencegah Path Traversal
                var cleanRelPath = relPath.Replace("..", "").TrimStart('/', '\\');
                var fullPath = Path.Combine(_downloadFolder, cleanRelPath);

                if (System.IO.File.Exists(fullPath))
                {
                    var fileNameOnly = Path.GetFileName(fullPath);
                    validFiles.Add((fullPath, fileNameOnly));
                }
            }

            if (validFiles.Count == 0)
                return NotFound("File tidak ditemukan di server.");

            // Single File (.mp3)
            if (validFiles.Count == 1)
            {
                var (fullPath, fileNameOnly) = validFiles[0];
                var fileBytes = System.IO.File.ReadAllBytes(fullPath);
                return File(fileBytes, "audio/mpeg", fileNameOnly);
            }

            // Multiple Files (.zip)
            using (var memoryStream = new MemoryStream())
            {
                using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var (fullPath, zipEntryName) in validFiles)
                    {
                        var zipEntry = zipArchive.CreateEntry(zipEntryName, CompressionLevel.Optimal);

                        using (var entryStream = zipEntry.Open())
                        using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            fileStream.CopyTo(entryStream);
                        }
                    }
                } // Stream ZIP selesai di-flush di sini

                memoryStream.Position = 0;
                byte[] zipBytes = memoryStream.ToArray();

                string zipFileName = $"hypen_playlist_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                return File(zipBytes, "application/zip", zipFileName);
            }
        }
    }
}
