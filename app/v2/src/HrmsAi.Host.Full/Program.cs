using HrmsAi.Access;
using HrmsAi.Chat;
using HrmsAi.Platform;
using HrmsAi.Resume;

var builder = WebApplication.CreateBuilder(args);
builder.AddHrmsAi(new AccessModule(), new ResumeModule(), new ChatModule());

var app = builder.Build();
app.UseHrmsAi();
app.Run();
