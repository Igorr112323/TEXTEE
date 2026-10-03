using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Visits11.Services;

public readonly record struct ListenResult(bool Ok, bool Lan, int Port, string? Error);

public sealed class PortalServer
{
    private readonly Dictionary<string, string> _resources = new(StringComparer.OrdinalIgnoreCase);
    private HttpListener? _listener;
    private int _port = 8090;
    private bool _indexed;

    public string TeacherName { get; set; } = string.Empty;
    public string Ssid { get; set; } = string.Empty;
    public int Port => _port;
    public bool IsRunning => _listener is { IsListening: true };

    public ListenResult Start(int preferredPort)
    {
        Stop();
        Index();
        var first = preferredPort is >= 1024 and <= 65535 ? preferredPort : 8090;
        for (var port = first; port < first + 8 && port <= 65535; port++)
        {
            if (TryListen($"http://+:{port}/", port, true, out var lan)) return lan;
            if (TryListen($"http://127.0.0.1:{port}/", port, false, out var local)) return local;
        }
        return new ListenResult(false, false, first, "Не удалось открыть порт. Укажите другой или закройте программу, которая его заняла.");
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        try { listener?.Stop(); } catch { }
        try { listener?.Close(); } catch { }
    }

    public static string? LocalIp()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up)
                .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Select(item => item.Address)
                .Where(item => item.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.ToString())
                .Where(item => !item.StartsWith("127.", StringComparison.Ordinal))
                .Distinct()
                .ToList();
            return addresses.FirstOrDefault(item => item.StartsWith("192.168.137.", StringComparison.Ordinal))
                ?? addresses.FirstOrDefault(item => item.StartsWith("192.168.", StringComparison.Ordinal))
                ?? addresses.FirstOrDefault(item => !item.StartsWith("169.254.", StringComparison.Ordinal))
                ?? addresses.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static string WifiPayload(string ssid, string password)
    {
        if (string.IsNullOrEmpty(password)) return $"WIFI:T:nopass;S:{Escape(ssid)};;";
        return $"WIFI:T:WPA;S:{Escape(ssid)};P:{Escape(password)};;";
    }

    private bool TryListen(string prefix, int port, bool lan, out ListenResult result)
    {
        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add(prefix);
            listener.Start();
            _listener = listener;
            _port = port;
            _ = Task.Run(Listen);
            result = new ListenResult(true, lan, port, lan ? null : "Телефоны пока не видят компьютер. Нажмите «Разрешить телефонам».");
            return true;
        }
        catch
        {
            try { listener.Close(); } catch { }
            result = default;
            return false;
        }
    }

    private async Task Listen()
    {
        while (_listener is { IsListening: true } listener)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch
            {
                break;
            }
            try
            {
                Handle(context);
            }
            catch
            {
                try { context.Response.Abort(); } catch { }
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var raw = context.Request.Url?.AbsolutePath ?? "/";
        var path = Uri.UnescapeDataString(raw);
        if (path.Contains("..", StringComparison.Ordinal))
        {
            WriteText(context, 400, "bad path");
            return;
        }
        if (path is "/" or "/install" or "/join")
        {
            var host = context.Request.Url?.Host;
            if (string.IsNullOrEmpty(host) || host == "+") host = "127.0.0.1";
            var origin = $"http://{host}:{context.Request.Url?.Port ?? _port}";
            WriteBytes(context, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(InstallPage(origin)), "no-store");
            return;
        }
        if (path == "/favicon.ico") path = "/logo.png";
        if (!_resources.TryGetValue(path, out var name))
        {
            WriteText(context, 404, "not found");
            return;
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null)
        {
            WriteText(context, 404, "not found");
            return;
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var ext = Path.GetExtension(path);
        var cache = ext is ".html" or ".js" or ".css" ? "no-store" : "public, max-age=86400";
        WriteBytes(context, 200, ContentType(path), buffer.ToArray(), cache);
    }

    private string InstallPage(string origin)
    {
        var teacher = string.IsNullOrWhiteSpace(TeacherName) ? "преподаватель" : WebUtility.HtmlEncode(TeacherName.Trim());
        var ssid = string.IsNullOrWhiteSpace(Ssid) ? "сеть аудитории" : WebUtility.HtmlEncode(Ssid.Trim());
        return $$"""
            <!doctype html>
            <html lang="ru">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Система автоматизированной переклички</title>
            <link rel="icon" href="/logo.png">
            <style>
            @font-face { font-family: Manrope; src: url("/fonts/Manrope-Regular.ttf") format("truetype"); font-weight: 400; }
            @font-face { font-family: Manrope; src: url("/fonts/Manrope-SemiBold.ttf") format("truetype"); font-weight: 600; }
            @font-face { font-family: Manrope; src: url("/fonts/Manrope-Bold.ttf") format("truetype"); font-weight: 700; }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100dvh; font-family: Manrope, sans-serif; background: #0A0D1A; color: #F0F2FA; }
            main { max-width: 440px; margin: 0 auto; padding: 36px 22px 48px; }
            img { width: 72px; height: 72px; border-radius: 18px; }
            h1 { font-size: 26px; line-height: 1.2; margin: 18px 0 8px; }
            p { color: #9099B8; font-size: 15px; line-height: 1.45; }
            ol { padding-left: 18px; color: #F0F2FA; }
            li { margin: 8px 0; }
            a.btn { display: block; text-align: center; text-decoration: none; background: #6366F1; color: white; border-radius: 12px; padding: 14px 16px; font-weight: 700; margin-top: 22px; }
            </style>
            </head>
            <body>
            <main>
            <img src="/logo.png" alt="КубГАУ">
            <h1>Система автоматизированной переклички</h1>
            <p>{{teacher}} · сеть «{{ssid}}». Интернет не нужен. Этим сайтом присутствие не отмечается: преподаватель сканирует QR с вашего экрана.</p>
            <ol>
            <li>Если вы ещё не в сети аудитории, сначала отсканируйте код Wi-Fi на экране преподавателя.</li>
            <li>Откройте приложение кнопкой ниже и введите логин с паролем.</li>
            <li>iPhone: Поделиться → На экран «Домой». Android-приложение, если оно уже стоит, тоже подходит.</li>
            <li>На паре покажите свой QR камере преподавателя.</li>
            </ol>
            <a class="btn" href="{{origin}}/index.html">Открыть приложение</a>
            </main>
            </body>
            </html>
            """;
    }

    private void Index()
    {
        if (_indexed) return;
        _indexed = true;
        foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            var path = ResourcePath(name);
            if (path.Length > 1) _resources[path] = name;
        }
    }

    private static string ResourcePath(string name)
    {
        var slash = name.Replace('\\', '/');
        var folder = slash.IndexOf("pwa/", StringComparison.OrdinalIgnoreCase);
        if (folder >= 0) return "/" + slash[(folder + 4)..];
        var dot = name.IndexOf(".pwa.", StringComparison.OrdinalIgnoreCase);
        if (dot < 0) return string.Empty;
        var relative = name[(dot + 5)..];
        if (relative.StartsWith("fonts.", StringComparison.OrdinalIgnoreCase))
            return "/fonts/" + relative["fonts.".Length..];
        if (relative.StartsWith("vendor.", StringComparison.OrdinalIgnoreCase))
            return "/vendor/" + relative["vendor.".Length..];
        return "/" + relative;
    }

    private static string ContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".webmanifest" => "application/manifest+json; charset=utf-8",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".ttf" => "font/ttf",
            ".txt" => "text/plain; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
    }

    private static void WriteText(HttpListenerContext context, int status, string text)
        => WriteBytes(context, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), "no-store");

    private static void WriteBytes(HttpListenerContext context, int status, string type, byte[] bytes, string cache)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = type;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.Headers["Cache-Control"] = cache;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
