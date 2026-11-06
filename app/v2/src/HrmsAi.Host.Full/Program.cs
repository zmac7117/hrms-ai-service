using HrmsAi.Access;
using HrmsAi.Chat;
using HrmsAi.Goals;
using HrmsAi.JobDescription;
using HrmsAi.Platform;
using HrmsAi.ResumeMatch;
using HrmsAi.Resume;

var builder = WebApplication.CreateBuilder(args);
builder.AddHrmsAi(new AccessModule(), new ResumeModule(), new JobDescriptionModule(), new GoalsModule(), new MatchModule(), new ChatModule());

var app = builder.Build();
app.UseHrmsAi();
app.Run();
