using System.Drawing;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ZXing;
using ZXing.QrCode;
using ZXing.Common;


namespace qrCodeRegressionWorker {
    static class Program {
        static async Task Main(string[] args) {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("app-settings.json", optional: false, reloadOnChange: true)
                .Build();

            // Read a setting from the configuration
            var qrCodeImagesDirectory = configuration["qrCodeImageDirectory"];
            
            var maxConcurrencySetting = configuration["maxConcurrentDownloads"];
            if (string.IsNullOrEmpty(maxConcurrencySetting)) {
                Console.WriteLine("maxConcurrentDownloads setting is missing or empty in the configuration.");
                return;
            }
            var maxConcurrency = int.Parse(maxConcurrencySetting);

            var iterationSettings = configuration["iterations"];
            if (string.IsNullOrEmpty(iterationSettings)) {
                Console.WriteLine("maxConcurrency setting is missing or empty in the configuration.");
                return;
            }
            var iterations = int.Parse(iterationSettings);



            Console.WriteLine($"qr-code image directory is: {qrCodeImagesDirectory}");

            if (!Directory.Exists(qrCodeImagesDirectory)) {
                Console.WriteLine("Directory does not exist.");
                return;
            }

            var imageFiles = Directory.GetFiles(qrCodeImagesDirectory, "*.png");

            if (imageFiles.Length == 0) {
                Console.WriteLine("No QR code images found in the directory.");
                return;
            }

            for (int i = 0; i < iterations; i++) {
                Console.WriteLine($">>>>> Iteration {i + 1} of {iterations} <<<<<");
                await Process(imageFiles, maxConcurrency);
                Console.WriteLine($">>>>> END of iteration {i + 1} <<<<<");
            }
        }

        static async Task Process(string[] imageFiles, int maxConcurrency) {
            // Create a SemaphoreSlim to limit the number of concurrent tasks
            using SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrency);

            var tasks = new List<Task>();
            var logs = new List<LogEntry>();

            foreach (var imagePath in imageFiles) {
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () => {
                    try {
                        var url = DecodeQRCode(imagePath);
                        if (url != null) {
                            var  log = await CallURL(url, imagePath);
                            logs.Add(log);
                        }
                        else {
                            Console.WriteLine($"Could not decode QR Code from {Path.GetFileName(imagePath)}.");
                        }
                    }
                    finally {
                        semaphore.Release();
                    }
                }));

                // If the number of tasks reaches maxConcurrency, wait for any of them to complete
                if (tasks.Count >= maxConcurrency) {
                    var completedTask = await Task.WhenAny(tasks);
                    tasks.Remove(completedTask);
                }
            }

            // Wait for the remaining tasks to complete
            await Task.WhenAll(tasks);
            await LogCallDetails(logs);

        }

        static string DecodeQRCode(string imagePath) {

            // sanity check
            if (string.IsNullOrEmpty(imagePath)) {
                Console.WriteLine("Image path is empty.");
                return string.Empty;
            }

            // sanity check
            if (!File.Exists(imagePath)) {
                Console.WriteLine("Image file does not exist.");
                return string.Empty;
            }

            var qrCodeReader = new QRCodeReader();
            var decodedText = string.Empty;

            // Load the image as a Bitmap
            using (var bitmap = (Bitmap)Image.FromFile(imagePath)) {
                // Convert Bitmap to LuminanceSource
                var luminanceSource = new BitmapLuminanceSource(bitmap);
                var binaryBitmap = new BinaryBitmap(new HybridBinarizer(luminanceSource));

                // Decode the QR code from the BinaryBitmap
                var result = qrCodeReader.decode(binaryBitmap);

                // Return or print the decoded text from the QR code
                if (result != null) {
                    Console.WriteLine("QR Code Content: " + result.Text);
                    decodedText = result.Text;
                }
                else {
                    Console.WriteLine("No QR code found in the image.");
                }
            }

            return decodedText;
        }

        static async Task<LogEntry> CallURL(string url, string imagePath) {
            using HttpClient client = new HttpClient();
            try {
                var response = await client.GetAsync(url);
                string statusCode = response.StatusCode.ToString();
                string responseBody = await response.Content.ReadAsStringAsync();

                // Log the call details
                //await LogCallDetails(url, imagePath, statusCode);
                return new LogEntry {
                    ImagePath = Path.GetFullPath(imagePath),
                    Url = url,
                    StatusCode = statusCode,
                    TimeOfCall = DateTime.UtcNow.ToString("o")
                };

            }
            catch (HttpRequestException e) {
                Console.WriteLine($"Request error for {url}: {e.Message}");
                // Log the error with an appropriate status code
                //await LogCallDetails(url, imagePath, "RequestError");
                return new LogEntry {
                    ImagePath = Path.GetFullPath(imagePath),
                    Url = url,
                    StatusCode = "RequestError",
                    TimeOfCall = DateTime.UtcNow.ToString("o")
                };
            }
        }

        static async Task LogCallDetails(List<LogEntry> logEntries) {
            string logFilePath = "log.json";

            // Read existing log or initialize a new list
            var existingLogEntries = new List<LogEntry>();
            if (File.Exists(logFilePath)) {
                var existingLog = await File.ReadAllTextAsync(logFilePath);
                existingLogEntries = JsonSerializer.Deserialize<List<LogEntry>>(existingLog) ?? new List<LogEntry>();
            }

            // Add the new log entries
            existingLogEntries.AddRange(logEntries);

            // Write the updated log back to the file
            var options = new JsonSerializerOptions { WriteIndented = true };
            string updatedLog = JsonSerializer.Serialize(existingLogEntries, options);

            using (FileStream fs = new FileStream(logFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)) {
                using (StreamWriter writer = new StreamWriter(fs)) {
                    await writer.WriteAsync(updatedLog);
                }
            }
        }

    }
}
