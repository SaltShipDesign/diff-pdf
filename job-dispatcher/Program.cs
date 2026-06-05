using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DiffPdf.JobDispatcher;

internal static class Program
{
    public static async Task<int> Main()
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            var config = WorkerConfig.FromEnvironment();
            using var api = new PepperApiClient(config);
            var uploader = new DiffUploader(config);
            var runner = new ProcessRunner(config.CommandTimeout);
            var worker = new DiffPdfWorker(config, api, uploader, runner);

            await worker.RunAsync(shutdown.Token);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Console.WriteLine("Shutdown requested.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}

internal sealed class DiffPdfWorker(
    WorkerConfig config,
    PepperApiClient api,
    DiffUploader uploader,
    ProcessRunner runner)
{
    private const string ScriptVersion = "1.0.0";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await api.AuthenticateAsync(cancellationToken);

        do
        {
            try
            {
                await ProcessNextJobAsync(cancellationToken);
            }
            catch (JobFailedException ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"Worker error: {ex.Message}");
            }

            if (config.RunOnce)
            {
                break;
            }

            await Task.Delay(config.PollInterval, cancellationToken);
        }
        while (!cancellationToken.IsCancellationRequested);
    }

    private async Task ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        var job = await api.GetJobAsync(cancellationToken);
        var now = DateTimeOffset.Now.ToString("d MMMM yyyy HH:mm");
        var processId = Environment.ProcessId;

        if (job is null)
        {
            Console.WriteLine($"{now} : Diff-PDF v{ScriptVersion} (Worker:{config.WorkerId}) process ID: {processId} : No Job");
            return;
        }

        Console.WriteLine($"{now} : Diff-PDF v{ScriptVersion} (Worker:{config.WorkerId}) process ID: {processId} : Starting Job ID: {job.Id}");

        var workerDirectory = Path.Combine(config.WorkDirectory, config.WorkerId);
        Directory.CreateDirectory(workerDirectory);

        var filePrefix = $"{processId}_{SanitizeFilePart(job.Id)}";
        var outputFile = Path.Combine(workerDirectory, $"{filePrefix}_output_file.pdf");
        var firstFile = Path.Combine(workerDirectory, $"{filePrefix}_first_file.pdf");
        var secondFile = Path.Combine(workerDirectory, $"{filePrefix}_second_file.pdf");

        try
        {
            await api.SetStatusAsync(job.Id, WorkerStatus.InProgress, null, cancellationToken);

            await DownloadFileAsync(job, first: true, firstFile, cancellationToken);
            await DownloadFileAsync(job, first: false, secondFile, cancellationToken);

            await CreateDiffAsync(job, firstFile, secondFile, outputFile, cancellationToken);
            await UploadDiffAsync(job, outputFile, cancellationToken);

            Console.WriteLine($"Completed Job {job.Id}.");
        }
        finally
        {
            if (config.DeleteArtifacts)
            {
                DeleteIfExists(firstFile);
                DeleteIfExists(secondFile);
                DeleteIfExists(outputFile);
            }
        }
    }

    private async Task DownloadFileAsync(PdfJob job, bool first, string destination, CancellationToken cancellationToken)
    {
        var versionId = first ? job.FirstVersionId : job.SecondVersionId;
        var remotePath = first ? job.FirstFile : job.SecondFile;
        var status = first ? WorkerStatus.GetFile1 : WorkerStatus.GetFile2;
        var errorStatus = first ? WorkerStatus.ErrorFile1 : WorkerStatus.ErrorFile2;
        var label = first ? "first" : "second";

        if (versionId.Length < 3)
        {
            await api.SetStatusAsync(job.Id, errorStatus, null, cancellationToken);
            throw new JobFailedException($"Error copying {label}_file_pdf: missing or invalid version id for {remotePath}");
        }

        await api.SetStatusAsync(job.Id, status, null, cancellationToken);
        var result = await runner.RunAsync("scp", [$"{config.ScpHostAlias}:{remotePath}", destination], cancellationToken);
        if (result.ExitCode != 0 || !File.Exists(destination))
        {
            await api.SetStatusAsync(job.Id, errorStatus, null, cancellationToken);
            throw new JobFailedException($"Error copying {label}_file_pdf: {result.Summary()}");
        }
    }

    private async Task CreateDiffAsync(PdfJob job, string firstFile, string secondFile, string outputFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(firstFile) || !File.Exists(secondFile))
        {
            await api.SetStatusAsync(job.Id, WorkerStatus.ErrorDiff, null, cancellationToken);
            throw new JobFailedException("Error: downloaded source file missing; diff-pdf cannot run.");
        }

        await api.SetStatusAsync(job.Id, WorkerStatus.GetDiff, null, cancellationToken);
        var result = await runner.RunAsync(
            config.DiffPdfPath,
            [firstFile, secondFile, $"--output-diff={outputFile}", config.DisplayNumber],
            cancellationToken);

        var response = result.CombinedOutput();
        await api.SetStatusAsync(job.Id, WorkerStatus.GetDiff, response, cancellationToken);

        if (result.ExitCode != 0 || !File.Exists(outputFile))
        {
            await api.SetStatusAsync(job.Id, WorkerStatus.ErrorDiff, null, cancellationToken);
            throw new JobFailedException($"Error running diff-pdf: {result.Summary()}");
        }
    }

    private async Task UploadDiffAsync(PdfJob job, string outputFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(outputFile))
        {
            await api.SetStatusAsync(job.Id, WorkerStatus.ErrorUploading, null, cancellationToken);
            throw new JobFailedException("Error uploading diff: output file does not exist.");
        }

        await api.SetStatusAsync(job.Id, WorkerStatus.Uploading, null, cancellationToken);
        var statusCode = await uploader.UploadAsync(job.Id, outputFile, cancellationToken);

        if (statusCode == HttpStatusCode.OK)
        {
            await api.SetStatusAsync(job.Id, WorkerStatus.Completed, null, cancellationToken);
            return;
        }

        await api.SetStatusAsync(job.Id, WorkerStatus.ErrorUploading, null, cancellationToken);
        throw new JobFailedException($"Error uploading diff: upload returned HTTP {(int)statusCode}.");
    }

    private static string SanitizeFilePart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        }

        return builder.Length == 0 ? "job" : builder.ToString();
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not delete {path}: {ex.Message}");
        }
    }
}

internal sealed class PepperApiClient : IDisposable
{
    private readonly WorkerConfig _config;
    private readonly HttpClient _httpClient = new();

    public PepperApiClient(WorkerConfig config)
    {
        _config = config;
        _httpClient.Timeout = config.HttpTimeout;
        _httpClient.DefaultRequestHeaders.Authorization = BasicAuth(config.ApiLogin, config.ApiPassword);
    }

    public async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        _ = await RequestAsync("auth", null, cancellationToken);
    }

    public async Task<PdfJob?> GetJobAsync(CancellationToken cancellationToken)
    {
        var data = await RequestAsync("pdfdiff/getJob", new Dictionary<string, string>
        {
            ["worker_id"] = _config.WorkerId
        }, cancellationToken);

        if (data.ValueKind == JsonValueKind.String &&
            string.Equals(data.GetString(), "No task available", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new UnexpectedValueException("Pepper getJob returned an unexpected response shape.");
        }

        return new PdfJob(
            RequiredString(data, "id"),
            RequiredString(data, "first_file"),
            RequiredString(data, "second_file"),
            RequiredString(data, "first_version_id"),
            RequiredString(data, "second_version_id"));
    }

    public async Task SetStatusAsync(string jobId, WorkerStatus status, string? response, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["id"] = jobId,
            ["status"] = ((int)status).ToString(),
            ["worker_id"] = _config.WorkerId
        };

        if (!string.IsNullOrEmpty(response))
        {
            fields["response"] = response;
        }

        _ = await RequestAsync("pdfdiff/setStatus", fields, cancellationToken);
    }

    private async Task<JsonElement> RequestAsync(string relativePath, Dictionary<string, string>? fields, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(_config.ApiEndpoint, relativePath.Trim('/') + "/");
        using var request = new HttpRequestMessage(fields is null ? HttpMethod.Get : HttpMethod.Post, requestUri);
        request.Headers.Authorization = BasicAuth(_config.ApiLogin, _config.ApiPassword);

        if (fields is not null)
        {
            request.Content = new FormUrlEncodedContent(fields);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new UnexpectedValueException($"Pepper returned invalid JSON from {requestUri}: {ex.Message}");
        }

        using (document)
        {
            if ((int)response.StatusCode is 400 or 401)
            {
                throw new UnexpectedValueException(ReadError(document.RootElement) ?? $"Pepper returned HTTP {(int)response.StatusCode}.");
            }

            if (!document.RootElement.TryGetProperty("response", out var responseElement) ||
                !responseElement.TryGetProperty("data", out var dataElement))
            {
                throw new UnexpectedValueException($"Pepper returned JSON without response.data from {requestUri}: {raw}");
            }

            return dataElement.Clone();
        }
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new UnexpectedValueException($"Pepper job is missing '{propertyName}'.");
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new UnexpectedValueException($"Pepper job field '{propertyName}' has unsupported JSON type {property.ValueKind}.")
        };
    }

    private static string? ReadError(JsonElement root)
    {
        if (root.TryGetProperty("response", out var response) &&
            response.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        return null;
    }

    private static AuthenticationHeaderValue BasicAuth(string username, string password)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    public void Dispose() => _httpClient.Dispose();
}

internal sealed class DiffUploader(WorkerConfig config)
{
    public async Task<HttpStatusCode> UploadAsync(string jobId, string outputFile, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = config.HttpTimeout };
        httpClient.DefaultRequestHeaders.Authorization = BasicAuth(config.ApiLogin, config.ApiPassword);

        await using var stream = File.OpenRead(outputFile);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var multipart = new MultipartFormDataContent
        {
            { new StringContent(config.WorkerId), "worker_id" },
            { new StringContent(jobId), "id" },
            { new StringContent(config.UploadHash), "hash" },
            { fileContent, "file", Path.GetFileName(outputFile) }
        };

        using var response = await httpClient.PostAsync(config.UploadUrl, multipart, cancellationToken);
        return response.StatusCode;
    }

    private static AuthenticationHeaderValue BasicAuth(string username, string password)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}

internal sealed class ProcessRunner(TimeSpan timeout)
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new JobFailedException($"Could not start process: {executable}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new ProcessResult(-1, await stdoutTask, await stderrTask + $"{Environment.NewLine}Timed out after {timeout}.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput()
    {
        return string.Join(Environment.NewLine, new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(value => value.Length > 0));
    }

    public string Summary()
    {
        var output = CombinedOutput();
        return output.Length == 0 ? $"exit code {ExitCode}" : $"exit code {ExitCode}: {output}";
    }
}

internal sealed record PdfJob(
    string Id,
    string FirstFile,
    string SecondFile,
    string FirstVersionId,
    string SecondVersionId);

internal sealed class WorkerConfig
{
    public Uri ApiEndpoint { get; init; } = null!;
    public Uri UploadUrl { get; init; } = null!;
    public string ApiLogin { get; init; } = string.Empty;
    public string ApiPassword { get; init; } = string.Empty;
    public string UploadHash { get; init; } = string.Empty;
    public string WorkerId { get; init; } = string.Empty;
    public string WorkDirectory { get; init; } = string.Empty;
    public string ScpHostAlias { get; init; } = string.Empty;
    public string DiffPdfPath { get; init; } = string.Empty;
    public string DisplayNumber { get; init; } = string.Empty;
    public TimeSpan PollInterval { get; init; }
    public TimeSpan CommandTimeout { get; init; }
    public TimeSpan HttpTimeout { get; init; }
    public bool RunOnce { get; init; }
    public bool DeleteArtifacts { get; init; }

    public static WorkerConfig FromEnvironment()
    {
        var apiEndpoint = GetUri("API_ENDPOINT", "https://pepper.saltship.com/api/");
        return new WorkerConfig
        {
            ApiEndpoint = apiEndpoint,
            UploadUrl = GetUri("PEPPER_UPLOAD_URL", new Uri(apiEndpoint, "pdfdiff/uploadDiff").ToString()),
            ApiLogin = GetRequired("PDFDIFF_API_LOGIN"),
            ApiPassword = GetRequired("PDFDIFF_API_PASSWORD"),
            UploadHash = GetRequired("API_PASSWORD"),
            WorkerId = Get("WORKER_ID", "292"),
            WorkDirectory = Get("WORK_DIR", "/work"),
            ScpHostAlias = Get("SCP_HOST_ALIAS", "pepper"),
            DiffPdfPath = Get("DIFF_PDF_PATH", "/usr/local/bin/diff-pdf"),
            DisplayNumber = Get("DIFF_PDF_DISPLAY", "99"),
            PollInterval = TimeSpan.FromSeconds(GetPositiveInt("POLL_INTERVAL_SECONDS", 5)),
            CommandTimeout = TimeSpan.FromSeconds(GetPositiveInt("COMMAND_TIMEOUT_SECONDS", 600)),
            HttpTimeout = TimeSpan.FromSeconds(GetPositiveInt("HTTP_TIMEOUT_SECONDS", 120)),
            RunOnce = GetBool("RUN_ONCE", false),
            DeleteArtifacts = GetBool("DELETE_ARTIFACTS", true)
        };
    }

    private static string Get(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string GetRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
        }

        return value;
    }

    private static Uri GetUri(string name, string defaultValue)
    {
        var value = Get(name, defaultValue);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Environment variable '{name}' must be an absolute URI.");
        }

        return uri;
    }

    private static int GetPositiveInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"Environment variable '{name}' must be a positive integer.");
        }

        return parsed;
    }

    private static bool GetBool(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal enum WorkerStatus
{
    Created = 0,
    Assigned = 10,
    InProgress = 20,
    GetFile1 = 21,
    GetFile2 = 22,
    GetDiff = 23,
    Uploading = 24,
    Completed = 30,
    ErrorFile1 = 90,
    ErrorFile2 = 90,
    ErrorDiff = 90,
    ErrorUploading = 90,
    Error = 99
}

internal sealed class JobFailedException(string message) : Exception(message);

internal sealed class UnexpectedValueException(string message) : Exception(message);
