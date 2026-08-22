using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeezerRpc.Core;

namespace DeezerRpc.Windows;

internal sealed class DiscordRpcClient : IAsyncDisposable
{
    private const int HandshakeOpcode = 0;
    private const int FrameOpcode = 1;
    private const int CloseOpcode = 2;
    private const int PingOpcode = 3;
    private const int PongOpcode = 4;
    private const int MaximumPayloadBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _applicationId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;

    public DiscordRpcClient(string applicationId) => _applicationId = applicationId;

    public bool IsConnected => _pipe?.IsConnected == true;
    public DiscordAccountProfile? Account { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await EnsureConnectedAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ResetConnection();
            throw new IOException("Discord n’a pas répondu dans le délai prévu.");
        }
        catch
        {
            ResetConnection();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SetActivityAsync(DiscordActivity activity, CancellationToken cancellationToken) =>
        SendActivityAsync(activity, cancellationToken);

    public Task ClearActivityAsync(CancellationToken cancellationToken) =>
        SendActivityAsync(null, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            ResetConnection();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task SendActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await EnsureConnectedAsync(timeout.Token);
            var command = new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Environment.ProcessId,
                    activity
                },
                nonce = Guid.NewGuid().ToString("N")
            };

            await WriteFrameAsync(_pipe!, FrameOpcode, JsonSerializer.Serialize(command, JsonOptions), timeout.Token);
            var response = await ReadApplicationFrameAsync(_pipe!, timeout.Token);
            if (response.Opcode == CloseOpcode || response.Payload.Contains("\"evt\":\"ERROR\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Discord a refusé la mise à jour Rich Presence.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ResetConnection();
            throw new IOException("Discord n’a pas répondu dans le délai prévu.");
        }
        catch
        {
            ResetConnection();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        for (var index = 0; index < 10; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new NamedPipeClientStream(
                ".",
                $"discord-ipc-{index}",
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            try
            {
                await candidate.ConnectAsync(150, cancellationToken);
                var handshake = JsonSerializer.Serialize(new { v = 1, client_id = _applicationId });
                await WriteFrameAsync(candidate, HandshakeOpcode, handshake, cancellationToken);
                var response = await ReadApplicationFrameAsync(candidate, cancellationToken);
                if (response.Opcode == CloseOpcode)
                {
                    candidate.Dispose();
                    continue;
                }

                _pipe = candidate;
                Account = ReadAccount(response.Payload);
                return;
            }
            catch (TimeoutException)
            {
                candidate.Dispose();
            }
            catch (IOException)
            {
                candidate.Dispose();
            }
        }

        throw new IOException("Discord Desktop n’est pas démarré ou son canal RPC est indisponible.");
    }

    private void ResetConnection()
    {
        _pipe?.Dispose();
        _pipe = null;
        Account = null;
    }

    private static DiscordAccountProfile? ReadAccount(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("user", out var user) ||
                !user.TryGetProperty("id", out var idElement))
            {
                return null;
            }

            var userId = idElement.GetString() ?? string.Empty;
            var username = ReadString(user, "username");
            var displayName = ReadString(user, "global_name");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = username;
            }

            var avatarHash = ReadString(user, "avatar");
            var avatarUrl = string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(avatarHash)
                ? string.Empty
                : $"https://cdn.discordapp.com/avatars/{userId}/{avatarHash}.png?size=128";

            return new DiscordAccountProfile
            {
                UserId = userId,
                DisplayName = displayName,
                Username = username,
                AvatarUrl = avatarUrl
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static async Task WriteFrameAsync(
        NamedPipeClientStream pipe,
        int opcode,
        string payload,
        CancellationToken cancellationToken)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), payloadBytes.Length);
        await pipe.WriteAsync(header, cancellationToken);
        await pipe.WriteAsync(payloadBytes, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    private static async Task<RpcFrame> ReadApplicationFrameAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(pipe, cancellationToken);
            if (frame.Opcode != PingOpcode)
            {
                return frame;
            }

            await WriteFrameAsync(pipe, PongOpcode, frame.Payload, cancellationToken);
        }
    }

    private static async Task<RpcFrame> ReadFrameAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await ReadExactlyAsync(pipe, header, cancellationToken);
        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (length is < 0 or > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Discord a envoyé une trame RPC invalide.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(pipe, payload, cancellationToken);
        return new RpcFrame(opcode, Encoding.UTF8.GetString(payload));
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Le canal RPC Discord a été fermé.");
            }

            offset += read;
        }
    }

    private sealed record RpcFrame(int Opcode, string Payload);
}
