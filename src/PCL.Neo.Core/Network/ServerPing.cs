using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Neo.Core.Network;

public sealed record ServerPingResult(
    string Host,
    int Port,
    long LatencyMs,
    string? Motd,
    int OnlinePlayers,
    int MaxPlayers,
    string? VersionName,
    int ProtocolVersion);

/// <summary>
/// 纯跨平台 Minecraft 经典服务端状态抓取器 (Server List Ping)。
/// 基于原生 TCP Socket + VarInt 协议包构造，不依赖任何第三方未移植组件。
/// </summary>
public static class ServerPing
{
    public static async Task<ServerPingResult> PingAsync(
        string host,
        int port = 25565,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
        sw.Stop();
        var connectLatency = sw.ElapsedMilliseconds;

        await using var stream = client.GetStream();

        // 1. Handshake Packet (id: 0x00, protocol: 47, host, port, nextState: 1)
        using var handshake = new MemoryStream();
        WriteVarInt(handshake, 0x00);
        WriteVarInt(handshake, 47); // 兼容性握手协议号
        WriteString(handshake, host);
        WriteUShort(handshake, (ushort)port);
        WriteVarInt(handshake, 1); // 1 = status

        SendPacket(stream, handshake.ToArray());

        // 2. Status Request Packet (id: 0x00, 空载荷)
        SendPacket(stream, [0x00]);

        // 3. 读取 Response Packet
        var packetLength = ReadVarInt(stream);
        var packetId = ReadVarInt(stream);
        if (packetId != 0x00)
            throw new InvalidOperationException($"服务端返回了非预期的数据包 ID: {packetId}");

        var jsonStr = ReadString(stream);
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        string? motd = null;
        if (root.TryGetProperty("description", out var desc))
        {
            if (desc.ValueKind == JsonValueKind.String) motd = desc.GetString();
            else if (desc.TryGetProperty("text", out var text)) motd = text.GetString();
        }

        int online = 0, max = 0;
        if (root.TryGetProperty("players", out var players))
        {
            if (players.TryGetProperty("online", out var on)) online = on.GetInt32();
            if (players.TryGetProperty("max", out var mx)) max = mx.GetInt32();
        }

        string? verName = null;
        int proto = 0;
        if (root.TryGetProperty("version", out var ver))
        {
            if (ver.TryGetProperty("name", out var vn)) verName = vn.GetString();
            if (ver.TryGetProperty("protocol", out var pr)) proto = pr.GetInt32();
        }

        return new ServerPingResult(host, port, connectLatency, motd, online, max, verName, proto);
    }

    private static void SendPacket(NetworkStream stream, byte[] payload)
    {
        var lenMs = new MemoryStream();
        WriteVarInt(lenMs, payload.Length);
        var lenBytes = lenMs.ToArray();
        stream.Write(lenBytes);
        stream.Write(payload);
        stream.Flush();
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var unsigned = (uint)value;
        while (true)
        {
            if ((unsigned & ~0x7Fu) == 0)
            {
                stream.WriteByte((byte)unsigned);
                return;
            }
            stream.WriteByte((byte)((unsigned & 0x7Fu) | 0x80u));
            unsigned >>= 7;
        }
    }

    private static int ReadVarInt(Stream stream)
    {
        int numRead = 0;
        int result = 0;
        byte read;
        do
        {
            var b = stream.ReadByte();
            if (b == -1) throw new EndOfStreamException();
            read = (byte)b;
            int value = read & 0x7F;
            result |= value << (7 * numRead);
            numRead++;
            if (numRead > 5) throw new InvalidOperationException("VarInt 太大");
        } while ((read & 0x80) != 0);

        return result;
    }

    private static void WriteString(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static string ReadString(Stream stream)
    {
        var len = ReadVarInt(stream);
        var buf = new byte[len];
        int total = 0;
        while (total < len)
        {
            var read = stream.Read(buf, total, len - total);
            if (read <= 0) throw new EndOfStreamException();
            total += read;
        }
        return Encoding.UTF8.GetString(buf);
    }

    private static void WriteUShort(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }
}
