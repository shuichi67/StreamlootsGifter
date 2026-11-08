using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamlootsManager;

public sealed class TwitchService : IAsyncDisposable
{
    // Twitch Client IDs are public identifiers. Set this to the Client ID for
    // the Streamloots Gifter application before producing a public release.
    public const string PublicClientId = "nfdfwb5pd969whjuhgj0p1fu858c93";
    private const string ApiRoot = "https://api.twitch.tv/helix/";
    private const string OAuthRoot = "https://id.twitch.tv/oauth2/";
    private const string EventSubUrl = "wss://eventsub.wss.twitch.tv/ws";
    private static readonly string[] Scopes = ["channel:read:redemptions", "user:read:chat"];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _clientId;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CancellationTokenSource? _eventSubStop;
    private Task? _eventSubTask;
    private string _accessToken = "";
    private string _refreshToken = "";
    private DateTimeOffset _tokenExpiresAt;
    private readonly object _chatFilterGate = new();
    private HashSet<string> _chatKeywords = new(StringComparer.OrdinalIgnoreCase);
    private long _lastChatActivityRaisedAt;

    private sealed class TwitchRateLimitException(TimeSpan retryAfter, string message) : Exception(message)
    {
        public TimeSpan RetryAfter { get; } = retryAfter;
    }

    public TwitchUser? User { get; private set; }
    public bool HasSavedSession => !string.IsNullOrWhiteSpace(_refreshToken);
    public bool IsChatReady { get; private set; }
    public event Action<string>? StatusChanged;
    public event Action<string, string>? DeviceCodeReady;
    public event Action<bool>? ChatReadyChanged;
    public event Action<RedemptionEvent>? RedemptionReceived;
    public event Action<ChatMessageEvent>? ChatMessageReceived;
    public event Action? ChatActivityReceived;

    public void SetChatKeywordFilter(IEnumerable<string> keywords)
    {
        var updated = new HashSet<string>(keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)).Select(keyword => keyword.Trim()), StringComparer.OrdinalIgnoreCase);
        lock (_chatFilterGate)
        {
            _chatKeywords = updated;
            _lastChatActivityRaisedAt = 0;
        }
    }

    public TwitchService(string? clientId = null)
    {
        _clientId = string.IsNullOrWhiteSpace(clientId) ? PublicClientId : clientId.Trim();
        if (TwitchTokenStore.Load() is { } saved)
        {
            _accessToken = saved.AccessToken;
            _refreshToken = saved.RefreshToken;
            _tokenExpiresAt = saved.ExpiresAt;
        }
    }

    public async Task<TwitchUser> LoginAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_clientId) || _clientId.StartsWith("SET_", StringComparison.Ordinal))
            throw new InvalidOperationException("The app's Twitch Client ID has not been configured by its publisher.");

        if (!string.IsNullOrWhiteSpace(_refreshToken))
        {
            try
            {
                var needsValidation = true;
                if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
                {
                    try
                    {
                        await ValidateTokenAsync(cancellationToken).ConfigureAwait(false);
                        needsValidation = false;
                    }
                    catch (TwitchAuthorizationExpiredException)
                    {
                        await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                }
                if (needsValidation) await ValidateTokenAsync(cancellationToken).ConfigureAwait(false);
                User = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
                StatusChanged?.Invoke($"Connected to {User.DisplayName}");
                return User;
            }
            catch (TwitchAuthorizationExpiredException)
            {
                ClearTokens();
            }
        }

        await AuthorizeWithDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        await ValidateTokenAsync(cancellationToken).ConfigureAwait(false);
        User = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        StatusChanged?.Invoke($"Connected to {User.DisplayName}");
        return User;
    }

    private async Task AuthorizeWithDeviceCodeAsync(CancellationToken cancellationToken)
    {
        StatusChanged?.Invoke("Requesting a Twitch sign-in code…");
        using var startResponse = await _http.PostAsync(OAuthRoot + "device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scopes"] = string.Join(' ', Scopes)
        }), cancellationToken).ConfigureAwait(false);
        var startJson = await startResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!startResponse.IsSuccessStatusCode) throw new InvalidOperationException($"Could not start Twitch sign-in: {startJson}");
        using var startDoc = JsonDocument.Parse(startJson);
        var start = startDoc.RootElement;
        var deviceCode = start.GetProperty("device_code").GetString() ?? "";
        var userCode = start.GetProperty("user_code").GetString() ?? "";
        var verificationUri = start.GetProperty("verification_uri").GetString() ?? "https://www.twitch.tv/activate";
        var interval = Math.Clamp(start.GetProperty("interval").GetInt32(), 1, 30);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(start.GetProperty("expires_in").GetInt32());
        if (deviceCode.Length == 0 || userCode.Length == 0) throw new InvalidDataException("Twitch returned an incomplete device sign-in response.");

        DeviceCodeReady?.Invoke(userCode, verificationUri);
        Process.Start(new ProcessStartInfo(verificationUri) { UseShellExecute = true });
        StatusChanged?.Invoke($"Enter code {userCode} on Twitch to authorize Streamloots Gifter.");

        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
            using var response = await _http.PostAsync(OAuthRoot + "token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["scopes"] = string.Join(' ', Scopes),
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }), cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var tokenDoc = JsonDocument.Parse(json);
                SetTokens(tokenDoc.RootElement);
                return;
            }

            var error = ReadOAuthError(json);
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval = Math.Min(interval + 5, 60); continue; }
            if (error is "access_denied" or "expired_token")
                throw new InvalidOperationException(error == "access_denied" ? "Twitch authorization was denied." : "The Twitch sign-in code expired. Please try again.");
            throw new InvalidOperationException($"Twitch sign-in failed: {json}");
        }
        throw new InvalidOperationException("The Twitch sign-in code expired. Please try again.");
    }

    private async Task<TwitchUser> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        using var users = await HelixJsonAsync("users", cancellationToken).ConfigureAwait(false);
        var userNode = users.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();
        if (userNode.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Could not retrieve the Twitch account.");
        return new TwitchUser(
            userNode.GetProperty("id").GetString() ?? "",
            userNode.GetProperty("login").GetString() ?? "",
            userNode.GetProperty("display_name").GetString() ?? "",
            userNode.TryGetProperty("profile_image_url", out var image) ? image.GetString() ?? "" : "");
    }

    private async Task ValidateTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OAuthRoot + "validate");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", _accessToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            throw new TwitchAuthorizationExpiredException("Twitch login needs to be renewed.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Twitch could not validate the saved login ({(int)response.StatusCode}).");
        using var doc = JsonDocument.Parse(json);
        var scopes = doc.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()).ToHashSet(StringComparer.Ordinal);
        if (Scopes.Any(scope => !scopes.Contains(scope)))
            throw new TwitchAuthorizationExpiredException("Twitch permissions changed. Please connect again and approve the requested permissions.");
    }

    private static string ReadOAuthError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? "" :
                doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    private sealed class TwitchAuthorizationExpiredException(string message) : Exception(message);

    public async Task<IReadOnlyList<TwitchReward>> GetRewardsAsync(CancellationToken cancellationToken)
    {
        if (User is null) throw new InvalidOperationException("Connect Twitch first.");
        using var doc = await HelixJsonAsync($"channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(User.Id)}", cancellationToken).ConfigureAwait(false);
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(reward => new TwitchReward(
            reward.GetProperty("id").GetString() ?? "",
            reward.GetProperty("title").GetString() ?? "",
            reward.GetProperty("cost").GetInt32(),
            reward.TryGetProperty("prompt", out var prompt) ? prompt.GetString() ?? "" : "",
            reward.TryGetProperty("is_enabled", out var enabled) && enabled.GetBoolean()
        )).ToArray();
    }

    public void StartEventSub()
    {
        if (User is null || (_eventSubTask is { IsCompleted: false })) return;
        SetChatReady(false);
        StatusChanged?.Invoke("Connecting to Twitch chat…");
        _eventSubStop = new CancellationTokenSource();
        _eventSubTask = RunEventSubAsync(_eventSubStop.Token);
    }

    private void SetChatReady(bool ready)
    {
        if (IsChatReady == ready) return;
        IsChatReady = ready;
        ChatReadyChanged?.Invoke(ready);
    }

    public async Task StopEventSubAsync()
    {
        _eventSubStop?.Cancel();
        if (_eventSubTask is not null)
        {
            try { await _eventSubTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _eventSubTask = null;
        _eventSubStop?.Dispose();
        _eventSubStop = null;
        SetChatReady(false);
    }

    public async Task LogoutAsync()
    {
        await StopEventSubAsync().ConfigureAwait(false);
        var tokens = new[] { _refreshToken, _accessToken }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
        foreach (var tokenValue in tokens)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var revoke = await _http.PostAsync(OAuthRoot + "revoke", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _clientId,
                    ["token"] = tokenValue
                }), timeout.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
        }
        User = null;
        ClearTokens();
        SetChatReady(false);
    }

    private void ClearTokens()
    {
        _accessToken = "";
        _refreshToken = "";
        _tokenExpiresAt = DateTimeOffset.MinValue;
        TwitchTokenStore.Delete();
    }

    private async Task RunEventSubAsync(CancellationToken token)
    {
        var reconnectUrl = EventSubUrl;
        var transferredSession = false;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenQueue = new Queue<string>();
        var retryDelay = TimeSpan.FromSeconds(2);
        while (!token.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(reconnectUrl), token).ConfigureAwait(false);
                StatusChanged?.Invoke("Connecting to Twitch chat…");
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var raw = await ReceiveMessageAsync(socket, token).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("metadata", out var metadata)) continue;
                    var type = metadata.TryGetProperty("message_type", out var typeNode) ? typeNode.GetString() : "";
                    if (type == "session_welcome")
                    {
                        var session = root.GetProperty("payload").GetProperty("session");
                        var sessionId = session.GetProperty("id").GetString() ?? "";
                        if (!transferredSession) await CreateSubscriptionsAsync(sessionId, token).ConfigureAwait(false);
                        transferredSession = false;
                        retryDelay = TimeSpan.FromSeconds(2);
                        var channelName = User?.DisplayName ?? User?.Login ?? "your channel";
                        SetChatReady(true);
                        StatusChanged?.Invoke($"Connected to {channelName}");
                    }
                    else if (type == "session_reconnect")
                    {
                        reconnectUrl = root.GetProperty("payload").GetProperty("session").GetProperty("reconnect_url").GetString() ?? EventSubUrl;
                        SetChatReady(false);
                        transferredSession = true;
                        break;
                    }
                    else if (type == "notification")
                    {
                        var messageId = metadata.GetProperty("message_id").GetString() ?? "";
                        if (messageId.Length > 0 && !seenIds.Add(messageId)) continue;
                        if (messageId.Length > 0)
                        {
                            seenQueue.Enqueue(messageId);
                            if (seenQueue.Count > 1024) seenIds.Remove(seenQueue.Dequeue());
                        }
                        DispatchNotification(root.GetProperty("payload"));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (TwitchRateLimitException ex)
            {
                SetChatReady(false);
                retryDelay = ex.RetryAfter;
                StatusChanged?.Invoke($"Twitch rate limited the chat connection. Retrying in {(int)Math.Ceiling(retryDelay.TotalSeconds)} seconds.");
            }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or JsonException or InvalidOperationException)
            {
                SetChatReady(false);
                StatusChanged?.Invoke($"Twitch listener reconnecting: {ex.Message}");
                retryDelay = TimeSpan.FromSeconds(Math.Min(30, Math.Max(2, retryDelay.TotalSeconds * 2)));
            }
            if (token.IsCancellationRequested) break;
            SetChatReady(false);
            if (!transferredSession) reconnectUrl = EventSubUrl;
            await Task.Delay(retryDelay, token).ConfigureAwait(false);
        }
    }

    private async Task CreateSubscriptionsAsync(string sessionId, CancellationToken token)
    {
        if (User is null) return;
        await CreateSubscriptionAsync("channel.channel_points_custom_reward_redemption.add", new { broadcaster_user_id = User.Id }, sessionId, token).ConfigureAwait(false);
        await CreateSubscriptionAsync("channel.chat.message", new { broadcaster_user_id = User.Id, user_id = User.Id }, sessionId, token).ConfigureAwait(false);
    }

    private async Task CreateSubscriptionAsync(string type, object condition, string sessionId, CancellationToken token)
    {
        var body = JsonSerializer.Serialize(new
        {
            type,
            version = "1",
            condition,
            transport = new { method = "websocket", session_id = sessionId }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoot + "eventsub/subscriptions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        await SetAuthHeadersAsync(request, token).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                TimeSpan? retryAfterHeader = response.Headers.RetryAfter?.Delta;
                if (retryAfterHeader is null && response.Headers.RetryAfter?.Date is { } retryDate)
                    retryAfterHeader = retryDate - DateTimeOffset.UtcNow;
                var retryAfter = retryAfterHeader ?? TimeSpan.FromSeconds(30);
                retryAfter = TimeSpan.FromSeconds(Math.Clamp(Math.Ceiling(retryAfter.TotalSeconds), 5, 120));
                throw new TwitchRateLimitException(retryAfter, $"Could not subscribe to {type}: {responseText}");
            }
            throw new InvalidOperationException($"Could not subscribe to {type}: {responseText}");
        }
    }

    private void DispatchNotification(JsonElement payload)
    {
        if (!payload.TryGetProperty("subscription", out var subscription) || !payload.TryGetProperty("event", out var evt)) return;
        var type = subscription.GetProperty("type").GetString();
        if (type == "channel.channel_points_custom_reward_redemption.add")
        {
            var reward = evt.GetProperty("reward");
            RedemptionReceived?.Invoke(new RedemptionEvent(
                evt.GetProperty("id").GetString() ?? "",
                evt.GetProperty("user_name").GetString() ?? "",
                evt.TryGetProperty("user_input", out var input) ? input.GetString() ?? "" : "",
                reward.GetProperty("id").GetString() ?? "",
                reward.GetProperty("title").GetString() ?? "",
                evt.TryGetProperty("user_login", out var login) ? login.GetString() ?? "" : ""));
        }
        else if (type == "channel.chat.message")
        {
            var message = evt.GetProperty("message");
            var text = message.TryGetProperty("text", out var messageText) ? messageText.GetString() ?? "" : "";
            var sourceBroadcaster = evt.TryGetProperty("source_broadcaster_user_id", out var source) ? source.GetString() : null;
            if (!string.IsNullOrEmpty(sourceBroadcaster)) return;
            var chatEvent = new ChatMessageEvent(
                evt.TryGetProperty("chatter_user_id", out var id) ? id.GetString() ?? "" : "",
                evt.TryGetProperty("chatter_user_login", out var login) ? login.GetString() ?? "" : "",
                evt.TryGetProperty("chatter_user_name", out var name) ? name.GetString() ?? "" : "",
                text);
            bool matchesKeyword;
            bool publishActivity;
            var now = Environment.TickCount64;
            lock (_chatFilterGate)
            {
                if (_chatKeywords.Count == 0) return;
                matchesKeyword = _chatKeywords.Contains(text.Trim());
                publishActivity = now - _lastChatActivityRaisedAt >= 3000;
                if (publishActivity) _lastChatActivityRaisedAt = now;
            }
            if (publishActivity) ChatActivityReceived?.Invoke();
            if (matchesKeyword) ChatMessageReceived?.Invoke(chatEvent);
        }
    }

    private async Task<JsonDocument> HelixJsonAsync(string endpoint, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + endpoint);
        await SetAuthHeadersAsync(request, token).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Twitch API returned {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private async Task SetAuthHeadersAsync(HttpRequestMessage request, CancellationToken token)
    {
        await EnsureFreshTokenAsync(token).ConfigureAwait(false);
        request.Headers.Add("Client-Id", _clientId);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
    }

    private async Task EnsureFreshTokenAsync(CancellationToken token)
    {
        if (DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2)) return;
        await _refreshLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2)) return;
            if (string.IsNullOrWhiteSpace(_refreshToken)) throw new TwitchAuthorizationExpiredException("Twitch login expired. Connect Twitch again.");
            await RefreshTokenAsync(token).ConfigureAwait(false);
        }
        finally { _refreshLock.Release(); }
    }

    private async Task RefreshTokenAsync(CancellationToken token)
    {
        using var response = await _http.PostAsync(OAuthRoot + "token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refreshToken
        }), token).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = ReadOAuthError(json);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized || error.Contains("invalid", StringComparison.OrdinalIgnoreCase))
                throw new TwitchAuthorizationExpiredException("Saved Twitch login expired or was revoked.");
            throw new InvalidOperationException($"Could not refresh Twitch login: {json}");
        }
        using var doc = JsonDocument.Parse(json);
        SetTokens(doc.RootElement);
    }

    private void SetTokens(JsonElement tokenResponse)
    {
        var access = tokenResponse.GetProperty("access_token").GetString() ?? "";
        var refresh = tokenResponse.TryGetProperty("refresh_token", out var refreshNode) ? refreshNode.GetString() ?? "" : "";
        if (access.Length == 0 || refresh.Length == 0)
            throw new InvalidDataException("Twitch did not return the access and refresh tokens needed to keep this connection safe.");
        var seconds = tokenResponse.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 14_400;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        // Persist the rotated refresh token before exposing the new session to API calls.
        TwitchTokenStore.Save(new StoredTwitchTokens(access, refresh, expiresAt));
        _accessToken = access;
        _refreshToken = refresh;
        _tokenExpiresAt = expiresAt;
    }

    private static async Task<string> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var content = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Twitch closed the EventSub connection.");
            content.Write(buffer, 0, result.Count);
            if (content.Length > 1_048_576) throw new InvalidDataException("EventSub message exceeded the size limit.");
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(content.GetBuffer(), 0, checked((int)content.Length));
    }

    public async ValueTask DisposeAsync()
    {
        await StopEventSubAsync().ConfigureAwait(false);
        _refreshLock.Dispose();
        _http.Dispose();
    }
}
