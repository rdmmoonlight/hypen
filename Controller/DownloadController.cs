using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace Hypen.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Route utama: api/download
    public class DownloadController : ControllerBase
    {
        private readonly string _downloadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "downloads");

        // Request URL: POST /api/download/download-to-client
        [HttpPost("download-to-client")]
        public IActionResult DownloadFilesToUser([FromBody] List<string> fileNames)
        {
            if (fileNames == null || fileNames.Count == 0)
                return BadRequest("Tidak ada file yang dipilih.");

            // 1. Skenario Single File
            if (fileNames.Count == 1)
            {
                var filePath = Path.Combine(_downloadFolder, fileNames[0]);
                if (!System.IO.File.Exists(filePath))
                    return NotFound($"File '{fileNames[0]}' tidak ditemukan.");

                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, "audio/mpeg", fileNames[0]);
            }

            // 2. Skenario Multi-File (ZIP)
            using (var memoryStream = new MemoryStream())
            {
                using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var fileName in fileNames)
                    {
                        var filePath = Path.Combine(_downloadFolder, fileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            var zipEntry = zipArchive.CreateEntry(fileName, CompressionLevel.Fastest);
                            using (var entryStream = zipEntry.Open())
                            using (var fileStream = System.IO.File.OpenRead(filePath))
                            {
                                fileStream.CopyTo(entryStream);
                            }
                        }
                    }
                }

                memoryStream.Position = 0;
                string zipFileName = $"audio_files_{DateTime.Now:yyyyMMddHHmmss}.zip";
                return File(memoryStream.ToArray(), "application/zip", zipFileName);
            }
        }
    }
}
