using HrmsAi.Access;
using HrmsAi.Chat;
using HrmsAi.Platform;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(@"C:\HRMS AI\config\models.json", optional: true, reloadOnChange: false);
builder.AddHrmsAi(new AccessModule(), new ChatModule());

var app = builder.Build();
app.UseHrmsAi();
app.Run();
