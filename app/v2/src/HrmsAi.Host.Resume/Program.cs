using HrmsAi.Access;
using HrmsAi.Platform;
using HrmsAi.Resume;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(@"C:\HRMS AI\config\models.json", optional: true, reloadOnChange: false);
builder.AddHrmsAi(new AccessModule(), new ResumeModule());

var app = builder.Build();
app.UseHrmsAi();
app.Run();
