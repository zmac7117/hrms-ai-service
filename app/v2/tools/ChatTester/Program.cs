using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

// Usage: dotnet run -- <hub url> <api key> [user name] [user id]
var url  = args.Length > 0 ? args[0] : "http://localhost:5200/api/chat/hub";
var key  = args.Length > 1 ? args[1] : "";
var name = args.Length > 2 ? args[2] : "Hamid";
var uid  = args.Length > 3 ? args[3] : "245";

var conn = new HubConnectionBuilder()
    .WithUrl(url, o => o.Headers.Add("X-API-Key", key))
    .WithAutomaticReconnect()
    .Build();

var finished = new SemaphoreSlim(0);
conn.On<JsonElement>("ReceiveToken", e => Console.Write(e.GetProperty("text").GetString()));
conn.On<JsonElement>("ReceiveDone", e =>
{
    Console.WriteLine();
    Console.WriteLine($"[done, tokens: {e.GetProperty("totalTokens").GetInt32()}]");
    finished.Release();
});
conn.On<JsonElement>("ReceiveError", e =>
{
    Console.WriteLine();
    Console.WriteLine($"[error {e.GetProperty("errorCode").GetString()}] {e.GetProperty("message").GetString()}");
    finished.Release();
});

// The real HRMS will do this part. Here we return fake data.
conn.On<JsonElement>("RunAction", async req =>
{
    var requestId = req.GetProperty("requestId").GetString();
    var action = req.GetProperty("action").GetString();
    var user   = req.GetProperty("externalUserId").GetString();
    Console.WriteLine($"\n[HRMS asked: {action} for user {user}]");

    object reply = action == "get_employee_profile"
        ? new
        {
            requestId,
            ok = true,
            data = new
            {
                employeeId = user,
                name,
                department = "Engineering",
                subDepartment = "Backend Development",
                designation = "Software Engineer",
                reportingManager = "Sara Khan",
                joiningDate = "2022-03-14",
                branch = "Kochi"
            }
        }
        : action == "get_leave_balance"
            ? (object)new
            {
                requestId,
                ok = true,
                data = new
                {
                    balances = new object[]
                    {
                        new { leaveType = "Casual Leave", total = 12, used = 3, remaining = 9 },
                        new { leaveType = "Sick Leave", total = 8, used = 0, remaining = 8 },
                        new { leaveType = "Earned Leave", total = 15, used = 5.5, remaining = 9.5 }
                    }
                }
            }
            : new { requestId, ok = false, error = "UNKNOWN_ACTION" };

    await conn.SendAsync("SubmitActionResult", reply);
});
await conn.StartAsync();
Console.WriteLine("Connected.");

var start = await conn.InvokeAsync<JsonElement>("StartChat",
    new { externalUserId = uid, userName = name, language = "en", conversationId = (Guid?)null });
var conversationId = start.GetProperty("conversationId").GetGuid();
Console.WriteLine("Bot: " + start.GetProperty("greeting").GetString());
Console.WriteLine("(type a question, empty line to quit)");

while (true)
{
    Console.Write("\nYou: ");
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) break;

    var sent = await conn.InvokeAsync<JsonElement>("SendMessage",
        new { conversationId, externalUserId = uid, text = line });
    if (!sent.GetProperty("accepted").GetBoolean())
    {
        Console.WriteLine("[rejected] " + sent.GetProperty("errorCode").GetString());
        continue;
    }
    Console.Write("Bot: ");
    await finished.WaitAsync();
}

await conn.StopAsync();