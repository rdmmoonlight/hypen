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
        public IActionResult DownloadFilesToUser([FromBody] List<string> fileNames)
        {
            if (fileNames == null || fileNames.Count == 0)
                return BadRequest("Tidak ada file yang dipilih.");

            // Filter hanya file yang benar-benar ada di server
            var validFiles = fileNames
                .Select(f => Path.Combine(_downloadFolder, Path.GetFileName(f)))
                .Where(System.IO.File.Exists)
                .ToList();

            if (validFiles.Count == 0)
                return NotFound("File tidak ditemukan di server.");

            // ==========================================
            // SKENARIO 1: HANYA 1 FILE
            // ==========================================
            if (validFiles.Count == 1)
            {
                var filePath = validFiles[0];
                var fileName = Path.GetFileName(filePath);
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, "audio/mpeg", fileName);
            }

            // ==========================================
            // SKENARIO 2: LEBIH DARI 1 FILE (ZIP)
            // ==========================================
            using (var memoryStream = new MemoryStream())
            {
                // PERBAIKAN KUNCI: Scope ZipArchive HARUS selesai (Disposed) 
                // SEBELUM memoryStream.ToArray() dipanggil!
                using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var filePath in validFiles)
                    {
                        var fileName = Path.GetFileName(filePath);
                        var zipEntry = zipArchive.CreateEntry(fileName, CompressionLevel.Optimal);

                        using (var entryStream = zipEntry.Open())
                        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            fileStream.CopyTo(entryStream);
                        }
                    }
                } // ZipArchive otomatis ditutup & di-flush di sini

                memoryStream.Position = 0;
                byte[] zipBytes = memoryStream.ToArray();

                string zipFileName = $"hypen_audio_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                return File(zipBytes, "application/zip", zipFileName);
            }
        }
    }
}
