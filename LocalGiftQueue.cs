using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace StreamlootsManager;

/// <summary>Persistent, loopback-only job queue consumed by the existing browser extension.</summary>
public sealed class LocalGiftQueue : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly object _gate = new();
    private readonly string _queuePath;
    private readonly string _journalPath;
    private readonly string _keyPath;
    private readonly List<QueueJob> _jobs;
    private readonly Dictionary<string, QueueJob> _jobsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QueueJob> _jobsByRedemptionId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _durabilityByRedemptionId = new(StringComparer.Ordinal);
    private readonly Queue<(QueueJob Job, string Version)> _pendingJobs = new();
    private readonly PriorityQueue<QueueJob, long> _pendingDeadlines = new();
    private readonly HashSet<string> _historyNameSet = new(StringComparer.OrdinalIgnoreCase);
    private string[] _historyNames = Array.Empty<string>();
    private readonly SemaphoreSlim _clientSlots = new(16, 16);
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<PersistenceCommand> _persistenceQueue = Channel.CreateUnbounded<PersistenceCommand>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Timer _pendingTimeoutTimer;
    private Task _persistenceTask = Task.CompletedTask;
    private long _queuedJournalSizeBytes;
    private DateTimeOffset? _lastExtensionActivity;
    private string _token = "";
    private TcpListener? _listener;
    private Task? _serverTask;

    private sealed record PersistenceCommand(string? JobJson, string? SnapshotJson, TaskCompletionSource<bool> Completion);

    public string Token { get { lock (_gate) return _token; } }
    public DateTimeOffset? LastExtensionActivity
    {
        get { lock (_gate) return _lastExtensionActivity; }
    }
    public event Action<QueueJob>? GiftUpdated;
    public event Func<StreamlootsCardFeedEvent, Task<StreamlootsCardFeedResult>>? StreamlootsCardReceived;
    public Func<string[]>? StreamlootsCardRuleNamesProvider { get; set; }
    public Func<string>? StreamlootsLanguageProvider { get; set; }

    public LocalGiftQueue(string queuePath, string keyPath)
    {
        _queuePath = queuePath;
        _journalPath = queuePath + ".journal";
        _keyPath = keyPath;
        _token = LoadOrCreateToken();
        _jobs = LoadJobs();
        foreach (var job in _jobs)
        {
            if (!string.IsNullOrWhiteSpace(job.JobId)) _jobsById[job.JobId] = job;
            if (!string.IsNullOrWhiteSpace(job.RedemptionId)) _jobsByRedemptionId[job.RedemptionId] = job;
            AddHistoryNameLocked(job.Viewer);
            AddHistoryNameLocked(job.Recipient);
        }
        SortHistoryNamesLocked();
        var changed = false;
        foreach (var job in _jobs.Where(j => j.Status == "processing"))
        {
            job.Status = "needs_attention";
            job.Detail = "The app stopped during this gift. Verify Streamloots before retrying.";
            changed = true;
        }
        foreach (var job in _jobs.Where(j => j.Status == "pending")) TrackPendingLocked(job);
        if (changed || File.Exists(_journalPath)) SaveLocked();
        _queuedJournalSizeBytes = File.Exists(_journalPath) ? new FileInfo(_journalPath).Length : 0;
        _persistenceTask = Task.Run(RunPersistenceWorkerAsync);
        _pendingTimeoutTimer = new Timer(_ => _ = ExpireStalePendingAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        _ = ExpireStalePendingAsync();
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

    public (string Status, string Detail) Enqueue(GiftRequest request) => EnqueueAsync(request).GetAwaiter().GetResult();

    public async Task<(string Status, string Detail)> EnqueueAsync(GiftRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RedemptionId)) return ("blocked", "Missing redemption ID.");
        if (string.IsNullOrWhiteSpace(request.Recipient))
            return ("blocked", request.Source == "streamloots_card" ? "The card event resolved no recipient. Check its redeemer username or entered-recipient field." : "Enter a Streamloots username in the Twitch redeem.");
        if (string.IsNullOrWhiteSpace(request.Collection))
            return ("blocked", request.Source == "streamloots_card" ? "Assign a collection to this Streamloots card rule." : "Assign a collection to this Twitch reward.");

        QueueJob? job = null;
        Task? persistence = null;
        Task? existingPersistence = null;
        string? existingStatus = null;
        lock (_gate)
        {
            if (_jobsByRedemptionId.TryGetValue(request.RedemptionId, out var existing))
            {
                existingStatus = existing.Status;
                _durabilityByRedemptionId.TryGetValue(request.RedemptionId, out existingPersistence);
            }
            else
            {
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
                _jobsById[job.JobId] = job;
                _jobsByRedemptionId[job.RedemptionId] = job;
                var namesChanged = AddHistoryNameLocked(job.Viewer) | AddHistoryNameLocked(job.Recipient);
                if (namesChanged) SortHistoryNamesLocked();
                persistence = SaveJobLocked(job);
                _durabilityByRedemptionId[job.RedemptionId] = persistence;
            }
        }
        if (job is null)
        {
            if (existingPersistence is not null) await existingPersistence.ConfigureAwait(false);
            lock (_gate)
                if (_jobsByRedemptionId.TryGetValue(request.RedemptionId, out var existing)) existingStatus = existing.Status;
            return (existingStatus ?? "pending", "This redemption was already queued.");
        }
        try { await persistence!.ConfigureAwait(false); }
        catch
        {
            lock (_gate)
            {
                _jobs.Remove(job);
                _jobsById.Remove(job.JobId);
                _jobsByRedemptionId.Remove(job.RedemptionId);
                _durabilityByRedemptionId.Remove(job.RedemptionId);
                RebuildHistoryNamesLocked();
            }
            throw;
        }
        lock (_gate)
        {
            _durabilityByRedemptionId.Remove(job.RedemptionId);
            if (job.Status == "pending" && _jobsById.TryGetValue(job.JobId, out var savedJob) && ReferenceEquals(savedJob, job))
                TrackPendingLocked(job);
        }
        GiftUpdated?.Invoke(job);
        return ("pending", "Queued for Streamloots.");
    }

    public IReadOnlyList<QueueJob> History(string? source = null)
    {
        lock (_gate)
            return _jobs.Where(j => source is null || j.Source == source)
                .Reverse()
                .Select(Clone).ToArray();
    }

    public int HistoryCount { get { lock (_gate) return _jobs.Count; } }

    public QueueJob? FindByRedemptionId(string redemptionId)
    {
        lock (_gate) return _jobsByRedemptionId.TryGetValue(redemptionId, out var job) ? Clone(job) : null;
    }

    public QueueJob? FindByJobId(string jobId)
    {
        lock (_gate) return _jobsById.TryGetValue(jobId, out var job) ? Clone(job) : null;
    }

    public string[] HistoryNames()
    {
        lock (_gate) return _historyNames;
    }

    public int CountMatchingHistory(string? username = null, string? source = null, string? status = null, DateTimeOffset? olderThan = null)
    {
        lock (_gate)
            return _jobs.Count(job =>
                (string.IsNullOrWhiteSpace(username) ||
                    job.Viewer.Contains(username, StringComparison.OrdinalIgnoreCase) ||
                    job.Recipient.Contains(username, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(source) || job.Source == source) &&
                (string.IsNullOrWhiteSpace(status) || job.Status == status) &&
                (olderThan is null || (DateTimeOffset.TryParse(job.CreatedAt, out var createdAt) && createdAt < olderThan.Value)));
    }

    public (IReadOnlyList<QueueJob> Jobs, bool HasMore) HistoryPage(string? source, IReadOnlyCollection<string>? statuses, string username, int skip, int take)
    {
        lock (_gate)
        {
            var pageSize = Math.Clamp(take, 1, 500);
            var page = new List<QueueJob>(pageSize);
            var skipped = 0;
            for (var index = _jobs.Count - 1; index >= 0; index--)
            {
                var job = _jobs[index];
                if (!string.IsNullOrWhiteSpace(source) && job.Source != source) continue;
                if (statuses is { Count: > 0 } && !statuses.Contains(job.Status)) continue;
                if (!string.IsNullOrWhiteSpace(username) &&
                    !job.Viewer.Contains(username, StringComparison.OrdinalIgnoreCase) &&
                    !job.Recipient.Contains(username, StringComparison.OrdinalIgnoreCase)) continue;
                if (skipped++ < Math.Max(0, skip)) continue;
                if (page.Count == pageSize) return (page, true);
                page.Add(Clone(job));
            }
            return (page, false);
        }
    }

    public int DeleteHistory(string? username = null, string? source = null, string? status = null, DateTimeOffset? olderThan = null) =>
        DeleteHistoryAsync(username, source, status, olderThan).GetAwaiter().GetResult();

    public async Task<int> DeleteHistoryAsync(string? username = null, string? source = null, string? status = null, DateTimeOffset? olderThan = null)
    {
        Task? persistence = null;
        var removed = 0;
        lock (_gate)
        {
            removed = _jobs.RemoveAll(job =>
                (string.IsNullOrWhiteSpace(username) ||
                    job.Viewer.Contains(username, StringComparison.OrdinalIgnoreCase) ||
                    job.Recipient.Contains(username, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(source) || job.Source == source) &&
                (string.IsNullOrWhiteSpace(status) || job.Status == status) &&
                (olderThan is null || (DateTimeOffset.TryParse(job.CreatedAt, out var createdAt) && createdAt < olderThan.Value)));
            if (removed > 0)
            {
                _jobsById.Clear();
                _jobsByRedemptionId.Clear();
                foreach (var job in _jobs)
                {
                    if (!string.IsNullOrWhiteSpace(job.JobId)) _jobsById[job.JobId] = job;
                    if (!string.IsNullOrWhiteSpace(job.RedemptionId)) _jobsByRedemptionId[job.RedemptionId] = job;
                }
                _pendingJobs.Clear();
                _pendingDeadlines.Clear();
                foreach (var job in _jobs.Where(job => job.Status == "pending")) TrackPendingLocked(job);
                RebuildHistoryNamesLocked();
                persistence = QueueSnapshotPersistLocked();
            }
        }
        if (persistence is not null) await persistence.ConfigureAwait(false);
        return removed;
    }

    public QueueJob? ClaimNext() => ClaimNextAsync().GetAwaiter().GetResult();

    public async Task<QueueJob?> ClaimNextAsync()
    {
        await ExpireStalePendingAsync().ConfigureAwait(false);
        QueueJob? claimed;
        Task persistence;
        lock (_gate)
        {
            QueueJob? job = null;
            while (_pendingJobs.TryDequeue(out var pending))
            {
                if (pending.Job.Status != "pending" || pending.Version != PendingVersion(pending.Job)) continue;
                job = pending.Job;
                break;
            }
            if (job is null) return null;
            job.Status = "processing";
            persistence = SaveJobLocked(job);
            claimed = Clone(job);
        }
        await persistence.ConfigureAwait(false);
        GiftUpdated?.Invoke(claimed);
        return claimed;
    }

    private async Task ExpireStalePendingAsync()
    {
        List<QueueJob> expired = [];
        List<Task> persistenceTasks = [];
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            while (_pendingDeadlines.TryPeek(out var job, out var deadlineTicks) && deadlineTicks <= now.UtcTicks)
            {
                _pendingDeadlines.Dequeue();
                if (job.Status != "pending" || PendingDeadlineTicks(job) != deadlineTicks) continue;
                job.Status = "needs_attention";
                job.Detail = "This gift stayed queued for over 5 minutes. Keep the Streamloots Activity Feed open and retry when the gift form is available.";
                job.UpdatedAt = now.ToString("O");
                expired.Add(Clone(job));
                persistenceTasks.Add(SaveJobLocked(job));
            }
        }
        if (persistenceTasks.Count > 0)
        {
            try { await Task.WhenAll(persistenceTasks).ConfigureAwait(false); }
            catch (Exception) { /* Keep the timer callback from surfacing an unobserved persistence exception. */ }
        }
        foreach (var job in expired) GiftUpdated?.Invoke(job);
    }

    public QueueJob? Complete(string jobId, string result, string detail) => CompleteAsync(jobId, result, detail).GetAwaiter().GetResult();

    public async Task<QueueJob?> CompleteAsync(string jobId, string result, string detail)
    {
        QueueJob? updated;
        Task persistence;
        lock (_gate)
        {
            var job = _jobsById.GetValueOrDefault(jobId);
            if (job?.Status != "processing") job = null;
            if (job is null) return null;
            job.Status = result switch
            {
                "sent" => "sent",
                "not_found" => "not_found",
                _ => "needs_attention"
            };
            job.Detail = detail.Length > 500 ? detail[..500] : detail;
            job.UpdatedAt = DateTimeOffset.Now.ToString("O");
            persistence = SaveJobLocked(job);
            updated = Clone(job);
        }
        await persistence.ConfigureAwait(false);
        GiftUpdated?.Invoke(updated);
        return updated;
    }

    public QueueJob? Retry(string jobId) => RetryAsync(jobId).GetAwaiter().GetResult();

    public async Task<QueueJob?> RetryAsync(string jobId)
    {
        QueueJob? updated;
        Task persistence;
        lock (_gate)
        {
            var job = _jobsById.GetValueOrDefault(jobId);
            if (job?.Status != "needs_attention") job = null;
            if (job is null) return null;
            var previousDetail = job.Detail.Trim();
            job.Status = "pending";
            job.Detail = previousDetail.Length == 0
                ? "Manual retry queued."
                : $"Manual retry queued. Previous issue: {previousDetail}";
            if (job.Detail.Length > 500) job.Detail = job.Detail[..500];
            job.UpdatedAt = DateTimeOffset.Now.ToString("O");
            persistence = SaveJobLocked(job);
            updated = Clone(job);
        }
        await persistence.ConfigureAwait(false);
        lock (_gate)
            if (updated.Status == "pending" && _jobsById.TryGetValue(updated.JobId, out var pendingJob) && pendingJob.Status == "pending")
                TrackPendingLocked(pendingJob);
        GiftUpdated?.Invoke(updated);
        return updated;
    }

    public QueueJob? MarkAsSent(string jobId) => MarkAsSentAsync(jobId).GetAwaiter().GetResult();

    public async Task<QueueJob?> MarkAsSentAsync(string jobId)
    {
        QueueJob? updated;
        Task persistence;
        lock (_gate)
        {
            var job = _jobsById.GetValueOrDefault(jobId);
            if (job?.Status != "needs_attention") job = null;
            if (job is null) return null;
            job.Status = "sent";
            job.Detail = "Marked as gifted after verifying the Streamloots result.";
            job.UpdatedAt = DateTimeOffset.Now.ToString("O");
            persistence = SaveJobLocked(job);
            updated = Clone(job);
        }
        await persistence.ConfigureAwait(false);
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
            if (!_clientSlots.Wait(0))
            {
                client.Dispose();
                continue;
            }
            _ = HandleClientSafeAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientSafeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await HandleClientAsync(client, requestTimeout.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or SocketException or JsonException or OperationCanceledException) { }
            finally { _clientSlots.Release(); }
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
            await WriteJsonAsync(stream, 200, new { job = await ClaimNextAsync().ConfigureAwait(false) }, cancellationToken, extensionOrigin);
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
            var handler = StreamlootsCardReceived;
            var outcome = handler is null
                ? new StreamlootsCardFeedResult(false, "The app is not handling Streamloots card events.")
                : await handler(new StreamlootsCardFeedEvent(eventId, cardName, redeemer, input, playedAt)).ConfigureAwait(false);
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
        var completed = await CompleteAsync(jobId, result, detail).ConfigureAwait(false);
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
        List<QueueJob> jobs = [];
        try
        {
            if (File.Exists(_queuePath))
                jobs = JsonSerializer.Deserialize<List<QueueJob>>(File.ReadAllText(_queuePath), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
        if (!File.Exists(_journalPath)) return jobs;

        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        var byRedemption = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < jobs.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(jobs[index].JobId)) byId[jobs[index].JobId] = index;
            if (!string.IsNullOrWhiteSpace(jobs[index].RedemptionId)) byRedemption[jobs[index].RedemptionId] = index;
        }
        try
        {
            foreach (var line in File.ReadLines(_journalPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                QueueJob? job;
                try { job = JsonSerializer.Deserialize<QueueJob>(line, JsonOptions); }
                catch (JsonException) { continue; } // Ignore a truncated final journal record.
                if (job is null) continue;
                if (!string.IsNullOrWhiteSpace(job.JobId) && byId.TryGetValue(job.JobId, out var index)) jobs[index] = job;
                else if (!string.IsNullOrWhiteSpace(job.RedemptionId) && byRedemption.TryGetValue(job.RedemptionId, out index)) jobs[index] = job;
                else
                {
                    var newIndex = jobs.Count;
                    jobs.Add(job);
                    if (!string.IsNullOrWhiteSpace(job.JobId)) byId[job.JobId] = newIndex;
                    if (!string.IsNullOrWhiteSpace(job.RedemptionId)) byRedemption[job.RedemptionId] = newIndex;
                }
            }
        }
        catch (IOException) { }
        return jobs;
    }

    private void SaveLocked()
    {
        var temporaryPath = _queuePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_jobs));
        File.Move(temporaryPath, _queuePath, true);
        if (File.Exists(_journalPath)) File.Delete(_journalPath);
    }

    private bool AddHistoryNameLocked(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return _historyNameSet.Add(name);
    }

    private void SortHistoryNamesLocked() => _historyNames = _historyNameSet
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void RebuildHistoryNamesLocked()
    {
        _historyNameSet.Clear();
        foreach (var job in _jobs)
        {
            AddHistoryNameLocked(job.Viewer);
            AddHistoryNameLocked(job.Recipient);
        }
        SortHistoryNamesLocked();
    }

    private void TrackPendingLocked(QueueJob job)
    {
        _pendingJobs.Enqueue((job, PendingVersion(job)));
        var deadlineTicks = PendingDeadlineTicks(job);
        if (deadlineTicks.HasValue) _pendingDeadlines.Enqueue(job, deadlineTicks.Value);
    }

    private static string PendingVersion(QueueJob job) => string.IsNullOrWhiteSpace(job.UpdatedAt) ? job.CreatedAt : job.UpdatedAt;

    private static long? PendingDeadlineTicks(QueueJob job)
    {
        var queuedAtText = string.IsNullOrWhiteSpace(job.UpdatedAt) ? job.CreatedAt : job.UpdatedAt;
        return DateTimeOffset.TryParse(queuedAtText, out var queuedAt)
            ? queuedAt.AddMinutes(5).UtcTicks
            : null;
    }

    private Task SaveJobLocked(QueueJob job)
    {
        var json = JsonSerializer.Serialize(job);
        var lineSize = Encoding.UTF8.GetByteCount(json) + 2;
        var compactAfterAppend = _queuedJournalSizeBytes + lineSize >= 8L * 1024 * 1024;
        var snapshotJson = compactAfterAppend ? JsonSerializer.Serialize(_jobs) : null;
        _queuedJournalSizeBytes = compactAfterAppend ? 0 : _queuedJournalSizeBytes + lineSize;
        return QueuePersistenceLocked(json, snapshotJson);
    }

    private Task QueueSnapshotPersistLocked()
    {
        _queuedJournalSizeBytes = 0;
        return QueuePersistenceLocked(null, JsonSerializer.Serialize(_jobs));
    }

    private Task QueuePersistenceLocked(string? jobJson, string? snapshotJson)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_persistenceQueue.Writer.TryWrite(new PersistenceCommand(jobJson, snapshotJson, completion)))
            completion.TrySetException(new IOException("The history persistence worker is not available."));
        return completion.Task;
    }

    private async Task RunPersistenceWorkerAsync()
    {
        await foreach (var command in _persistenceQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (command.JobJson is not null)
                {
                    var bytes = Encoding.UTF8.GetBytes(command.JobJson + "\r\n");
                    await using var stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    await stream.WriteAsync(bytes).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                if (command.SnapshotJson is not null)
                {
                    var temporaryPath = _queuePath + ".tmp";
                    await File.WriteAllTextAsync(temporaryPath, command.SnapshotJson).ConfigureAwait(false);
                    File.Move(temporaryPath, _queuePath, true);
                    if (File.Exists(_journalPath)) File.Delete(_journalPath);
                }
                command.Completion.TrySetResult(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_gate)
                    _queuedJournalSizeBytes = File.Exists(_journalPath) ? new FileInfo(_journalPath).Length : 0;
                command.Completion.TrySetException(ex);
            }
        }
    }

    private static QueueJob Clone(QueueJob job) => new()
    {
        JobId = job.JobId,
        RedemptionId = job.RedemptionId,
        Recipient = job.Recipient,
        Viewer = job.Viewer,
        Source = job.Source,
        Keyword = job.Keyword,
        Collection = job.Collection,
        Packs = job.Packs,
        Cards = job.Cards,
        Status = job.Status,
        Detail = job.Detail,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
        PlayedAt = job.PlayedAt
    };

    public async ValueTask DisposeAsync()
    {
        _pendingTimeoutTimer.Dispose();
        _stop.Cancel();
        _listener?.Stop();
        if (_serverTask is not null)
        {
            try { await _serverTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _persistenceQueue.Writer.TryComplete();
        await _persistenceTask.ConfigureAwait(false);
        _stop.Dispose();
    }
}
