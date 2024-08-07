using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using static System.Net.Mime.MediaTypeNames;
using System.Linq;

namespace EncryptFiles
{
    public static class EncryptFiles
    {
        private static readonly ILogger _logger;
        public static string containerName { get; set; } = "contractdoc";
        public static string connectionString { get; set; }

        public static string FilePath { get; set; }
        

        [FunctionName("EncryptFiles")]
        public static async Task<IActionResult> Encrypt([HttpTrigger(AuthorizationLevel.Function, "post", Route = "encrypt")] HttpRequest req,
            ILogger log)    
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                //var file = paramData.File;
                connectionString = data.connectionString;
                FilePath = data.FilePath;
                if (string.IsNullOrEmpty(connectionString))
                {
                    return new BadRequestObjectResult("Connection String is not provided");
                }
                var file = await GetBlobContentViaLinkAsync(FilePath);

                string encryptionKey = data.encryptionKey;

                if (string.IsNullOrEmpty(encryptionKey))
                {
                    return new BadRequestObjectResult("ENCRYPTION_KEY environment variable is not set.");
                }

                byte[] encryptedContent = Encrypt(file, encryptionKey);
                await CreateFile(encryptedContent, FilePath);
                // return new FileContentResult(encryptedContent, "application/octet-stream");

                var desc = new ReturnFileResult
                {
                    File = new FileContentResult(encryptedContent, "application/octet-stream"),
                    FileName = Path.GetFileName(FilePath)
                };
                return new OkObjectResult(desc);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.InnerException);

                throw;
            }

            
        }

        private static byte[] Encrypt(byte[] data, string key)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Encoding.UTF8.GetBytes(key);
                aesAlg.GenerateIV();
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    msEncrypt.Write(BitConverter.GetBytes(aesAlg.IV.Length), 0, sizeof(int));
                    msEncrypt.Write(aesAlg.IV, 0, aesAlg.IV.Length);
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        csEncrypt.Write(data, 0, data.Length);
                        csEncrypt.FlushFinalBlock();
                    }
                    return msEncrypt.ToArray();
                }
            }
        }

        [FunctionName("DecryptFiles")]
        public static async Task<IActionResult> Decrypt(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "decrypt")] HttpRequest req)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                //var file = paramData.File;
                connectionString = data.connectionString;
                FilePath = data.FilePath;
                //IFormFile file = formCollection.Files["file"];
                //if (file == null || file.Length == 0)
                //{
                //    return new BadRequestObjectResult("File not provided or empty.");
                //}

                //// Process the file (e.g., save it, read content)
                //string fileContent;
                //using (var reader = new StreamReader(file.OpenReadStream()))
                //{
                //    fileContent = await reader.ReadToEndAsync();
                //}

                (byte[] encryptedContent, string filename) = await GetBlobContentViaPathAsync(FilePath);
                if(string.IsNullOrEmpty(filename))  return new NotFoundResult();
                string encryptionKey = data.encryptionKey;
                if (string.IsNullOrEmpty(encryptionKey))
                {
                    return new BadRequestObjectResult("ENCRYPTION_KEY environment variable is not set.");
                }
                byte[] decryptedContent = Decrypt(encryptedContent, encryptionKey);
                return new FileContentResult(decryptedContent, "application/octet-stream");
                //await File.WriteAllBytesAsync("javra12.png", decryptedContent);
                //var desc = new ReturnFileResult
                //{
                //    File = new FileContentResult(decryptedContent, "application/octet-stream"),
                //    FileName = Path.GetFileName(filename)
                //};
                //return new OkObjectResult(desc);
            }
            catch (Exception ex)
            {
                return new FileContentResult(null, "application/octet-stream");
                throw;
            }
            
        }

        [FunctionName("GetFileName")]
        public static async Task<IActionResult> GetFileName(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "getFilename")] HttpRequest req)
        {

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            //var file = paramData.File;
            connectionString = data.connectionString;
            FilePath = data.FilePath;
            //IFormFile file = formCollection.Files["file"];
            //if (file == null || file.Length == 0)
            //{
            //    return new BadRequestObjectResult("File not provided or empty.");
            //}

            //// Process the file (e.g., save it, read content)
            //string fileContent;
            //using (var reader = new StreamReader(file.OpenReadStream()))
            //{
            //    fileContent = await reader.ReadToEndAsync();
            //}

            (byte[] encryptedContent, string filename) = await GetBlobContentViaPathAsync(FilePath);
            if (string.IsNullOrEmpty(filename)) return new NotFoundResult();


            string encryptionKey = data.encryptionKey;
            if (string.IsNullOrEmpty(encryptionKey))
            {
                return new BadRequestObjectResult("ENCRYPTION_KEY environment variable is not set.");
            }
            
            return new OkObjectResult(Path.GetFileName( filename));
        }

        private static byte[] Decrypt(byte[] data, string key)
        {
            using (MemoryStream msDecrypt = new MemoryStream(data))
            {
                byte[] ivLengthBytes = new byte[sizeof(int)];
                msDecrypt.Read(ivLengthBytes, 0, ivLengthBytes.Length);
                int ivLength = BitConverter.ToInt32(ivLengthBytes, 0);

                byte[] iv = new byte[ivLength];
                msDecrypt.Read(iv, 0, iv.Length);

                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.Key = Encoding.UTF8.GetBytes(key);
                    aesAlg.IV = iv;
                    ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (MemoryStream msPlain = new MemoryStream())
                        {
                            csDecrypt.CopyTo(msPlain);
                            return msPlain.ToArray();
                        }
                    }
                }
            }
        }

        private static async Task CreateFile(byte[] content, string blobName)
        {

            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);

            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            await containerClient.CreateIfNotExistsAsync();

            BlobClient blobClient = containerClient.GetBlobClient(blobName);


            using (MemoryStream ms = new MemoryStream(content))
            {
                await blobClient.UploadAsync(ms, true);
            }
        }

        private static async Task<byte[]> GetBlobContentViaLinkAsync(string path)
        {
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            BlobClient blobClient = containerClient.GetBlobClient(path);

            if (await blobClient.ExistsAsync())
            {
                BlobDownloadInfo download = await blobClient.DownloadAsync();
                using (MemoryStream ms = new MemoryStream())
                {
                    await download.Content.CopyToAsync(ms);
                    return ms.ToArray();
                }
            }
            return null;
        }

        private static async Task<(byte[], string)> GetBlobContentViaPathAsync(string path)
        {
            try
            {
                BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var filename = containerClient.GetBlobs(prefix: path).FirstOrDefault();
                if (!string.IsNullOrEmpty(filename?.Name))
                {
                    var blobClient = containerClient.GetBlobClient($"{filename.Name}");
                    if (await blobClient.ExistsAsync())
                    {
                        BlobDownloadInfo download = await blobClient.DownloadAsync();
                        using (MemoryStream ms = new MemoryStream())
                        {
                            await download.Content.CopyToAsync(ms);
                            return (ms.ToArray(), filename.Name);
                        }
                    }
                }
                return (null, null);
            }
            catch (Exception ex)
            {
                return (null, null);
            }
            
        }

    }

}
