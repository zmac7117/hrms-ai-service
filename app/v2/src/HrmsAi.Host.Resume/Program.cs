using HrmsAi.Access;
using HrmsAi.Platform;
using HrmsAi.Resume;

var builder = WebApplication.CreateBuilder(args);
builder.AddHrmsAi(new AccessModule(), new ResumeModule());

var app = builder.Build();
app.UseHrmsAi();
app.Run();
