using System.Text.Json;
using System.Diagnostics;

if (args is ["--child"])
{
    await Task.Delay(TimeSpan.FromMinutes(3));
    return;
}

var home = Environment.GetEnvironmentVariable("CODEX_HOME");
var leakedKey = Environment.GetEnvironmentVariable("SLOP_TEST_API_KEY") is not null;
var greeting = JsonSerializer.Serialize(new { method = "fake/ready", @params = new { home, leakedKey } });
Console.Out.WriteLine(greeting);
Console.Out.Flush();

long? pendingApprovalRequest = null;
var accountMode = args.Contains("--api-key-mode") ? "apiKey" : "none";
var loginId = Guid.Parse("11111111-2222-4333-8444-555555555555");
while (Console.In.ReadLine() is { } line)
{
    using var document = JsonDocument.Parse(line);
    var root = document.RootElement;
    if (!root.TryGetProperty("method", out var methodElement))
    {
        if (pendingApprovalRequest is { } pending && root.TryGetProperty("id", out var responseId) &&
            responseId.ValueKind == JsonValueKind.String && responseId.GetString() == "approval-1")
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new { id = pending, result = new { accepted = true } }));
            Console.Out.Flush();
            pendingApprovalRequest = null;
        }

        continue;
    }

    var method = methodElement.GetString();
    var hasId = root.TryGetProperty("id", out var id);
    if (!hasId)
    {
        continue;
    }

    switch (method)
    {
        case "initialize":
            Console.Out.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result = new { userAgent = "fake" } }));
            Console.Out.Flush();
            break;
        case "account/read":
            object? account = accountMode switch
            {
                "chatgpt" => new { type = "chatgpt", email = "SECRET_EMAIL@example.invalid", planType = "pro" },
                "apiKey" => new { type = "apiKey" },
                _ => null,
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                id = id.GetInt64(), result = new { account, requiresOpenaiAuth = true },
            }));
            Console.Out.Flush();
            break;
        case "account/login/start":
            var loginType = root.GetProperty("params").GetProperty("type").GetString();
            if (loginType != "chatgpt")
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), error = new { code = -1, message = "unsupported" } }));
                Console.Out.Flush();
                break;
            }

            var authUrl = args.Contains("--bad-url") ? "https://evil.invalid/steal" : "https://chatgpt.com/auth/start";
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                id = id.GetInt64(), result = new { type = "chatgpt", loginId, authUrl },
            }));
            if (!args.Contains("--hold-login"))
            {
                accountMode = "chatgpt";
                Console.Out.WriteLine(JsonSerializer.Serialize(new
                {
                    method = "account/login/completed", @params = new { loginId, success = true, error = (string?)null },
                }));
            }

            Console.Out.Flush();
            break;
        case "account/login/cancel":
            Console.Out.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result = new { } }));
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                method = "account/login/completed", @params = new { loginId, success = false, error = "SECRET_LOGIN_ERROR" },
            }));
            Console.Out.Flush();
            break;
        case "account/logout":
            accountMode = "none";
            Console.Out.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result = new { } }));
            Console.Out.WriteLine("{\"method\":\"account/updated\",\"params\":{\"authMode\":null,\"planType\":null}}");
            Console.Out.Flush();
            break;
        case "test/echo":
            var value = root.GetProperty("params").GetProperty("value").GetInt32();
            var responseId = id.Clone();
            _ = Task.Run(async () =>
            {
                await Task.Delay(value == 1 ? 80 : 10);
                Console.Out.WriteLine(JsonSerializer.Serialize(new
                {
                    id = responseId.GetInt64(), result = new { value },
                }));
                Console.Out.Flush();
            });
            break;
        case "test/error":
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                id = id.GetInt64(), error = new { code = 42, message = "SECRET_RESPONSE_BODY" },
            }));
            Console.Out.Flush();
            break;
        case "test/notify":
            Console.Out.WriteLine("{\"method\":\"test/event\",\"params\":{\"ok\":true}}");
            Console.Out.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result = true }));
            Console.Out.Flush();
            break;
        case "test/serverRequest":
            pendingApprovalRequest = id.GetInt64();
            Console.Out.WriteLine("{\"id\":\"approval-1\",\"method\":\"test/approve\",\"params\":{\"action\":\"read\"}}");
            Console.Out.Flush();
            break;
        case "test/afterResponse":
            Console.Out.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result = new { seen = true } }));
            Console.Out.Flush();
            break;
        case "test/hang":
            break;
        case "test/oversize":
            Console.Out.WriteLine("{\"method\":\"test/large\",\"params\":{\"data\":\"" + new string('x', 8192) + "\"}}");
            Console.Out.Flush();
            break;
        case "test/stderrFlood":
            Console.Error.Write(new string('x', 8192));
            Console.Error.Flush();
            await Task.Delay(TimeSpan.FromSeconds(10));
            break;
        case "test/invalid":
            Console.Out.WriteLine("not-json");
            Console.Out.Flush();
            break;
        case "test/spawnChild":
            var childInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            childInfo.ArgumentList.Add("--child");
            var child = Process.Start(childInfo)!;
            Console.Out.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result = new { pid = child.Id } }));
            Console.Out.Flush();
            break;
    }
}
