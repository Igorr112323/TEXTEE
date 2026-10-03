using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Visits11.Services;

public static class QrProtocol
{
    public const int WindowSeconds = 5;
    public const int WindowSlack = 9;
    public const int MultiChunk = 320;

    public readonly record struct StudentTicket(string Login, string Password, string Device, long Window, string Sig);

    public readonly record struct Frame(char Kind, string RollId, int Index, int Count, string Chunk);

    public readonly record struct RosterEntry(int Id, string Login, string Hash, string Name, string Device);

    public readonly record struct Mark(char Code, int Id, string Extra);

    public static string PasswordHash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static string Sign(string login, string password, string device, long window)
    {
        var key = Encoding.UTF8.GetBytes(password);
        var msg = Encoding.UTF8.GetBytes(login + "\n" + device + "\n" + window.ToString());
        var mac = HMACSHA256.HashData(key, msg);
        return Convert.ToHexString(mac.AsSpan(0, 4)).ToLowerInvariant();
    }

    public static long CurrentWindow(DateTimeOffset? now = null)
        => (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / WindowSeconds;

    public static bool WindowFresh(long window, DateTimeOffset? now = null)
    {
        var delta = CurrentWindow(now) - window;
        if (delta < 0) delta = -delta;
        return delta <= WindowSlack;
    }

    public static string StudentPayload(string login, string password, string device, long? window = null)
    {
        var value = window ?? CurrentWindow();
        return $"KG1|S|{login}|{password}|{device}|{value}|{Sign(login, password, device, value)}";
    }

    public static bool TryParseStudent(string text, out StudentTicket ticket)
    {
        ticket = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('|');
        if (parts.Length != 7 || parts[0] != "KG1" || parts[1] != "S") return false;
        if (parts[2].Length == 0 || parts[3].Length == 0 || parts[4].Length == 0 || parts[6].Length == 0) return false;
        if (!long.TryParse(parts[5], out var window)) return false;
        ticket = new StudentTicket(parts[2], parts[3], parts[4], window, parts[6].ToLowerInvariant());
        return true;
    }

    public static bool SignatureValid(StudentTicket ticket)
        => string.Equals(Sign(ticket.Login, ticket.Password, ticket.Device, ticket.Window), ticket.Sig, StringComparison.OrdinalIgnoreCase);

    public static bool TryParseFrame(string text, out Frame frame)
    {
        frame = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('|', 6);
        if (parts.Length < 6 || parts[0] != "KG1" || parts[1].Length != 1) return false;
        var kind = parts[1][0];
        if (kind is not ('C' or 'R')) return false;
        if (!int.TryParse(parts[3], out var index) || !int.TryParse(parts[4], out var count)) return false;
        if (index < 1 || count < 1 || index > count || count > 80 || parts[2].Length == 0) return false;
        frame = new Frame(kind, parts[2].ToLowerInvariant(), index, count, parts[5]);
        return true;
    }

    public static List<string> EncodeFrames(char kind, string rollId, string body, int chunkSize)
    {
        var b64 = Convert.ToBase64String(Compress(body));
        var chunks = new List<string>();
        if (b64.Length == 0)
        {
            chunks.Add(string.Empty);
        }
        else
        {
            for (var i = 0; i < b64.Length; i += chunkSize)
            {
                chunks.Add(b64.Substring(i, Math.Min(chunkSize, b64.Length - i)));
            }
        }

        var frames = new List<string>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            frames.Add($"KG1|{kind}|{rollId}|{i + 1}|{chunks.Count}|{chunks[i]}");
        }
        return frames;
    }

    public static List<string> EncodeSession(string rollId, string body)
    {
        var single = EncodeFrames('C', rollId, body, 100000);
        if (single.Count == 1 && single[0].Length <= 1200) return single;
        return EncodeFrames('C', rollId, body, MultiChunk);
    }

    public static List<string> EncodeResult(string rollId, string body)
    {
        var single = EncodeFrames('R', rollId, body, 100000);
        if (single.Count == 1 && single[0].Length <= 1400) return single;
        return EncodeFrames('R', rollId, body, MultiChunk);
    }

    public static bool TryDecode(IEnumerable<Frame> frames, out string body)
    {
        body = string.Empty;
        var list = frames.ToList();
        if (list.Count == 0) return false;
        var count = list[0].Count;
        var roll = list[0].RollId;
        var kind = list[0].Kind;
        if (list.Count != count) return false;
        var ordered = new string[count];
        foreach (var frame in list)
        {
            if (frame.Count != count || frame.RollId != roll || frame.Kind != kind) return false;
            ordered[frame.Index - 1] = frame.Chunk;
        }
        if (ordered.Any(chunk => chunk is null)) return false;
        try
        {
            body = Decompress(Convert.FromBase64String(string.Concat(ordered)));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string BuildSessionBody(IEnumerable<(int Id, string Login, string Password, string Name, string? Device)> students)
    {
        var builder = new StringBuilder();
        foreach (var student in students)
        {
            builder.Append(student.Id).Append('\t')
                .Append(Sanitize(student.Login)).Append('\t')
                .Append(PasswordHash(student.Password)).Append('\t')
                .Append(Sanitize(student.Name)).Append('\t')
                .Append(Sanitize(student.Device ?? string.Empty)).Append('\n');
        }
        return builder.ToString();
    }

    public static List<RosterEntry> ParseRoster(string body)
    {
        var list = new List<RosterEntry>();
        foreach (var line in body.Split('\n'))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length < 5 || !int.TryParse(parts[0], out var id)) continue;
            list.Add(new RosterEntry(id, parts[1], parts[2], parts[3], parts[4]));
        }
        return list;
    }

    public static string BuildResultBody(IEnumerable<(int Id, string Device)> present, IEnumerable<(int OwnerId, string Attempted)> suspicious)
    {
        var builder = new StringBuilder();
        foreach (var row in present)
        {
            builder.Append('P').Append('\t').Append(row.Id).Append('\t').Append(Sanitize(row.Device)).Append('\n');
        }
        foreach (var row in suspicious)
        {
            builder.Append('S').Append('\t').Append(row.OwnerId).Append('\t').Append(Sanitize(row.Attempted)).Append('\n');
        }
        return builder.ToString();
    }

    public static List<Mark> ParseMarks(string body)
    {
        var list = new List<Mark>();
        foreach (var line in body.Split('\n'))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length < 3 || parts[0].Length != 1 || !int.TryParse(parts[1], out var id)) continue;
            list.Add(new Mark(parts[0][0], id, parts[2]));
        }
        return list;
    }

    public static string NewRollId()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '\t' or '\n' or '\r' or '|') chars[i] = ' ';
        }
        return new string(chars).Trim();
    }

    public static byte[] Compress(string text)
    {
        var input = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(input, 0, input.Length);
        }
        return output.ToArray();
    }

    public static string Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public sealed class Collector
    {
        private string? _roll;
        private char _kind;
        private int _count;
        private readonly Dictionary<int, string> _chunks = new();

        public int Got => _chunks.Count;
        public int Count => _count;

        public void Reset()
        {
            _roll = null;
            _kind = default;
            _count = 0;
            _chunks.Clear();
        }

        public bool Add(Frame frame, out string? body)
        {
            body = null;
            if (_roll != frame.RollId || _kind != frame.Kind || _count != frame.Count)
            {
                _roll = frame.RollId;
                _kind = frame.Kind;
                _count = frame.Count;
                _chunks.Clear();
            }
            _chunks[frame.Index] = frame.Chunk;
            if (_chunks.Count < _count) return false;
            var frames = new List<Frame>(_count);
            for (var i = 1; i <= _count; i++)
            {
                if (!_chunks.TryGetValue(i, out var chunk)) return false;
                frames.Add(new Frame(_kind, _roll!, i, _count, chunk));
            }
            if (!TryDecode(frames, out var text)) return false;
            body = text;
            return true;
        }
    }
}
