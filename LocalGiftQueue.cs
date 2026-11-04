using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamlootsManager;

/// <summary>Persistent, loopback-only job queue consumed by the existing browser extension.</summary>
public sealed class LocalGiftQueue : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly object _gate = new();
    private readonly string _queuePath;
    private readonly string _keyPath;
    private readonly List<QueueJob> _jobs;
    private readonly CancellationTokenSource _stop = new();
    private readonly Timer _pendingTimeoutTimer;
    private DateTimeOffset? _lastExtensionActivity;
    private string _token = "";
    private TcpListener? _listener;
    private Task? _serverTask;

    public string Token { get { lock (_gate) return _token; } }
    public DateTimeOffset? LastExtensionActivity
    {
        get { lock (_gate) return _lastExtensionActivity; }
    }
    public event Action<QueueJob>? GiftUpdated;
    public event Func<StreamlootsCardFeedEvent, StreamlootsCardFeedResult>? StreamlootsCardReceived;
    public Func<string[]>? StreamlootsCardRuleNamesProvider { get; set; }
    public Func<string>? StreamlootsLanguageProvider { get; set; }

    public LocalGiftQueue(string queuePath, string keyPath)
    {
        _queuePath = queuePath;
        _keyPath = keyPath;
        _token = LoadOrCreateToken();
        _jobs = LoadJobs();
        var changed = false;
        foreach (var job in _jobs.Where(j => j.Status == "processing"))
        {
            job.Status = "needs_attention";
            job.Detail = "The app stopped during this gift. Verify Streamloots before retrying.";
            changed = true;
        }
        if (changed) SaveLocked();
        _pendingTimeoutTimer = new Timer(_ => ExpireStalePending(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        ExpireStalePending();
    }

    public void Start()
    {
        if (_listener is not null) return;
        _listener = new TcpListener(IPAddress.Loopback, 8765);
        _listener.Start();
        _serverTask = RunServerAsync(_stop.Token);
    }

    public string RotateToken()
    {
        lock (_gate)
        {
            var token = CreateToken();
            var temporaryPath = _keyPath + ".tmp";
            File.WriteAllText(temporaryPath, token);
            File.Move(temporaryPath, _keyPath, true);
            _token = token;
            _lastExtensionActivity = null;
            return token;
        }
    }

    public (string Status, string Detail) Enqueue(GiftRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RedemptionId)) return ("blocked", "Missing redemption ID.");
        if (string.IsNullOrWhiteSpace(request.Recipient))
            return ("blocked", request.Source == "streamloots_card" ? "The card event resolved no recipient. Check its redeemer username or entered-recipient field." : "Enter a Streamloots username in the Twitch redeem.");
        if (string.IsNullOrWhiteSpace(request.Collection))
            return ("blocked", request.Source == "streamloots_card" ? "Assign a collection to this Streamloots card rule." : "Assign a collection to this Twitch reward.");

        QueueJob job;
        lock (_gate)
        {
            var existing = _jobs.FirstOrDefault(j => j.RedemptionId == request.RedemptionId);
            if (existing is not null) return (existing.Status, "This redemption was already queued.");
            job = new QueueJob
            {
                JobId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                RedemptionId = request.RedemptionId,
                Recipient = request.Recipient.Trim(),
                Viewer = request.Viewer.Trim(),
                Source = request.Source,
                Keyword = request.Keyword,
                Collection = request.Collection.Trim(),
                Packs = Math.Clamp(request.Packs, 1, 50),
                Cards = Math.Clamp(request.Cards, 1, 3),
                Status = "pending",
                CreatedAt = DateTimeOffset.Now.ToString("O"),
                PlayedAt = request.PlayedAt
            };
            _jobs.Add(job);
            SaveLocked();
        }
        GiftUpdated?.Invoke(job);
        return ("pending", "Queued for Streamloots.");
    }

    public IReadOnlyList<QueueJob> History(string? source = null)
    {
        lock (_gate)
            return _jobs.Where(j => source is null || j.Source == source)
                .OrderByDescending(j => j.CreatedAt, StringComparer.Ordinal)
                .Select(Clone).ToArray();
    }

    public int DeleteHistory(string? username = null, string? source = null, string? status = null, DateTimeOffset? olderThan = null)
    {
        lock (_gate)
        {
            var removed = _jobs.RemoveAll(job =>
                (string.IsNullOrWhiteSpace(username) ||
                    job.Viewer.Contains(username, StringComparison.OrdinalIgnoreCase) ||
                    job.Recipient.Contains(username, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(source) || job.Source == source) &&
                (string.IsNullOrWhiteSpace(status) || job.Status == status) &&
                (olderThan is null || (DateTimeOffset.TryParse(job.CreatedAt, out var createdAt) && createdAt < olderThan.Value)));
            if (removed > 0) SaveLocked();
            return removed;
        }
    }

    public QueueJob? ClaimNext()
    {
        ExpireStalePending();
        QueueJob? claimed;
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Status == "pending");
            if (job is null) return null;
            job.Status = "processing";
            SaveLocked();
            claimed = Clone(job);
        }
        GiftUpdated?.Invoke(claimed);
        return claimed;
    }

    private void ExpireStalePending()
    {
        List<QueueJob> expired = [];
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            foreach (var job in _jobs.Where(job => job.Status == "pending"))
            {
                var queuedAtText = string.IsNullOrWhiteSpace(job.UpdatedAt) ? job.CreatedAt : job.UpdatedAt;
                if (!DateTimeOffset.TryParse(queuedAtText, out var queuedAt) || now - queuedAt < TimeSpan.FromMinutes(5)) continue;
                job.Status = "needs_attention";
                job.Detail = "This gift stayed queued for over 5 minutes. Keep the Streamloots Activity Feed open and retry when the gift form is available.";
                job.UpdatedAt = now.ToString("O");
                expired.Add(Clone(job));
            }
            if (expired.Count > 0) SaveLocked();
        }
        foreach (var job in expired) GiftUpdated?.Invoke(job);
    }

    public QueueJob? Complete(string jobId, string result, string detail)
    {
        QueueJob? updated;
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId && j.Status == "processing");
            if (job is null) return null;
            job.Status = result switch
            {
                "sent" => "sent",
                "not_found" => "not_found",
                _ => "needs_attention"
            };
            job.Detail = detail.Length > 500 ? detail[..500] : detail;
            job.UpdatedAt = DateTimeOffset.Now.ToString("O");
            SaveLocked();
            updated = Clone(job);
        }
        GiftUpdated?.Invoke(updated);
        return updated;
    }

    public QueueJob? Retry(string jobId)
    {
        QueueJob? updated;
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId && j.Status == "needs_attention");
            if (job is null) return null;
            var previousDetail = job.Detail.Trim();
            job.Status = "pending";
            job.Detail = previousDetail.Length == 0
                ? "Manual retry queued."
                : $"Manual retry queued. Previous issue: {previousDetail}";
            if (job.Detail.Length > 500) job.Detail = job.Detail[..500];
            job.UpdatedAt = DateTimeOffset.Now.ToString("O");
            SaveLocked();
            updated = Clone(job);
        }
        GiftUpdated?.Invoke(updated);
        return updated;
    }

    public QueueJob? MarkAsSent(string jobId)
    {
        QueueJob? updated;
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId && j.Status == "needs_attention");
            if (job is null) return null;
            job.Status = "sent";
            job.Detail = "Marked as gifted after verifying the Streamloots result.";
            job.UpdatedAt = DateTimeOffset.Now.ToString("O");
            SaveLocked();
            updated = Clone(job);
        }
        GiftUpdated?.Invoke(updated);
        return updated;
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            _ = HandleClientSafeAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientSafeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try { await HandleClientAsync(client, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or SocketException or JsonException or OperationCanceledException) { }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var headerBytes = new List<byte>(1024);
        var one = new byte[1];
        while (headerBytes.Count < 16_384)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0) return;
            headerBytes.Add(one[0]);
            var n = headerBytes.Count;
            if (n >= 4 && headerBytes[n - 4] == 13 && headerBytes[n - 3] == 10 && headerBytes[n - 2] == 13 && headerBytes[n - 1] == 10) break;
        }
        if (headerBytes.Count >= 16_384) { await WriteResponseAsync(stream, 431, "Request header too large", cancellationToken); return; }

        var header = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;
        var requestParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2) { await WriteResponseAsync(stream, 400, "Invalid request", cancellationToken); return; }
        var method = requestParts[0];
        var path = requestParts[1].Split('?')[0];
        var headers = lines.Skip(1).Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        headers.TryGetValue("Origin", out var origin);
        var extensionOrigin = IsExtensionOrigin(origin) ? origin : null;
        if (method == "OPTIONS")
        {
            if (extensionOrigin is null) { await WriteResponseAsync(stream, 403, "Forbidden", cancellationToken); return; }
            await WriteResponseAsync(stream, 204, "", cancellationToken, corsOrigin: extensionOrigin);
            return;
        }
        if (method == "GET" && path == "/pair")
        {
            if (extensionOrigin is null) { await WriteJsonAsync(stream, 403, new { error = "Pairing is only available to the browser extension." }, cancellationToken); return; }
            await WriteJsonAsync(stream, 200, new { token = Token }, cancellationToken, extensionOrigin);
            return;
        }
        if (method == "GET" && path == "/health")
        {
            if (headers.TryGetValue("Authorization", out var healthAuthorization) && !TokenMatches(healthAuthorization))
            {
                await WriteJsonAsync(stream, 401, new { error = "Bridge key is invalid." }, cancellationToken, extensionOrigin);
                return;
            }
            await WriteJsonAsync(stream, 200, new { ok = true }, cancellationToken, extensionOrigin);
            return;
        }
        if (path is not ("/next" or "/complete" or "/rotate" or "/card-redeemed" or "/card-rules" or "/extension-settings")) { await WriteJsonAsync(stream, 404, new { error = "Not found" }, cancellationToken); return; }
        if (origin is not null && extensionOrigin is null) { await WriteJsonAsync(stream, 403, new { error = "Requests from this origin are not allowed." }, cancellationToken); return; }
        if (!headers.TryGetValue("Authorization", out var authorization) || !TokenMatches(authorization))
        {
            await WriteJsonAsync(stream, 401, new { error = "Unauthorized" }, cancellationToken, extensionOrigin);
            return;
        }
        if (method == "POST" && path == "/rotate")
        {
            if (extensionOrigin is null) { await WriteJsonAsync(stream, 403, new { error = "Key rotation requires the browser extension." }, cancellationToken); return; }
            var newToken = RotateToken();
            await WriteJsonAsync(stream, 200, new { token = newToken }, cancellationToken, extensionOrigin);
            return;
        }
        lock (_gate) _lastExtensionActivity = DateTimeOffset.UtcNow;
        if (method == "GET" && path == "/next")
        {
            await WriteJsonAsync(stream, 200, new { job = ClaimNext() }, cancellationToken, extensionOrigin);
            return;
        }
        if (method == "GET" && path == "/card-rules")
        {
            var names = StreamlootsCardRuleNamesProvider?.Invoke() ?? [];
            await WriteJsonAsync(stream, 200, new { card_names = names }, cancellationToken, extensionOrigin);
            return;
        }
        if (method == "GET" && path == "/extension-settings")
        {
            var language = StreamlootsLanguageProvider?.Invoke() ?? "English";
            await WriteJsonAsync(stream, 200, new { streamloots_language = language }, cancellationToken, extensionOrigin);
            return;
        }
        if (method != "POST" || path is not ("/complete" or "/card-redeemed")) { await WriteJsonAsync(stream, 404, new { error = "Not found" }, cancellationToken); return; }
        if (!headers.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, out var length) || length is < 0 or > 16_384)
        {
            await WriteJsonAsync(stream, 400, new { error = "Invalid content length" }, cancellationToken, extensionOrigin);
            return;
        }
        var body = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            offset += read;
        }
        if (offset != length) { await WriteJsonAsync(stream, 400, new { error = "Incomplete request body" }, cancellationToken, extensionOrigin); return; }
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (path == "/card-redeemed")
        {
            var eventId = root.TryGetProperty("event_id", out var eventIdNode) ? eventIdNode.GetString() ?? "" : "";
            var cardName = root.TryGetProperty("card_name", out var cardNameNode) ? cardNameNode.GetString() ?? "" : "";
            var redeemer = root.TryGetProperty("redeemer", out var redeemerNode) ? redeemerNode.GetString() ?? "" : "";
            var input = root.TryGetProperty("input", out var inputNode) ? inputNode.GetString() ?? "" : "";
            var playedAt = root.TryGetProperty("played_at", out var playedAtNode) ? playedAtNode.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(cardName))
            {
                await WriteJsonAsync(stream, 400, new { error = "Card event needs an event ID and card name." }, cancellationToken, extensionOrigin);
                return;
            }
            lock (_gate) _lastExtensionActivity = DateTimeOffset.UtcNow;
            var outcome = StreamlootsCardReceived?.Invoke(new StreamlootsCardFeedEvent(eventId, cardName, redeemer, input, playedAt))
                ?? new StreamlootsCardFeedResult(false, "The app is not handling Streamloots card events.");
            if (!outcome.Accepted)
            {
                await WriteJsonAsync(stream, 422, new { accepted = false, error = outcome.Message }, cancellationToken, extensionOrigin);
                return;
            }
            await WriteJsonAsync(stream, 200, new { accepted = true, message = outcome.Message }, cancellationToken, extensionOrigin);
            return;
        }
        var jobId = root.TryGetProperty("job_id", out var jobIdNode) ? jobIdNode.GetString() ?? "" : "";
        var result = root.TryGetProperty("result", out var resultNode) ? resultNode.GetString() ?? "needs_attention" : "needs_attention";
        var detail = root.TryGetProperty("detail", out var detailNode) ? detailNode.GetString() ?? "" : "";
        var completed = Complete(jobId, result, detail);
        if (completed is null) await WriteJsonAsync(stream, 404, new { error = "Job not found" }, cancellationToken, extensionOrigin);
        else await WriteJsonAsync(stream, 200, new { ok = true }, cancellationToken, extensionOrigin);
    }

    private bool TokenMatches(string authorization)
    {
        var expected = Encoding.UTF8.GetBytes("Bearer " + Token);
        var actual = Encoding.UTF8.GetBytes(authorization);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static async Task WriteJsonAsync(NetworkStream stream, int status, object body, CancellationToken token, string? corsOrigin = null)
    {
        await WriteResponseAsync(stream, status, JsonSerializer.Serialize(body), token, "application/json; charset=utf-8", corsOrigin);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, CancellationToken token, string contentType = "text/plain; charset=utf-8", string? corsOrigin = null)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 431 => "Request Header Fields Too Large", _ => "Error" };
        var allowOrigin = corsOrigin ?? "*";
        var responseHeader = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {bodyBytes.Length}\r\nAccess-Control-Allow-Origin: {allowOrigin}\r\nAccess-Control-Allow-Headers: Authorization, Content-Type\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Private-Network: true\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHeader), token).ConfigureAwait(false);
        if (bodyBytes.Length > 0) await stream.WriteAsync(bodyBytes, token).ConfigureAwait(false);
    }

    private string LoadOrCreateToken()
    {
        try
        {
            var existing = File.ReadAllText(_keyPath).Trim();
            if (!string.IsNullOrEmpty(existing)) return existing;
        }
        catch (IOException) { }
        var token = CreateToken();
        File.WriteAllText(_keyPath, token);
        return token;
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsExtensionOrigin(string? origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "chrome-extension" || uri.Host.Length != 32 || uri.AbsolutePath != "/") return false;
        return uri.Host.All(character => character is >= 'a' and <= 'p');
    }

    private List<QueueJob> LoadJobs()
    {
        try
        {
            if (!File.Exists(_queuePath)) return [];
            return JsonSerializer.Deserialize<List<QueueJob>>(File.ReadAllText(_queuePath), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    private void SaveLocked()
    {
        var temporaryPath = _queuePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_jobs, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, _queuePath, true);
    }

    private static QueueJob Clone(QueueJob job) => JsonSerializer.Deserialize<QueueJob>(JsonSerializer.Serialize(job))!;

    public async ValueTask DisposeAsync()
    {
        _pendingTimeoutTimer.Dispose();
        _stop.Cancel();
        _listener?.Stop();
        if (_serverTask is not null)
        {
            try { await _serverTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _stop.Dispose();
    }
}
