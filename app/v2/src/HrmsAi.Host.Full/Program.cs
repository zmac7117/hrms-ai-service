using HrmsAi.Access;
using HrmsAi.Chat;
using HrmsAi.Goals;
using HrmsAi.Invoices;
using HrmsAi.JobDescription;
using HrmsAi.Platform;
using HrmsAi.Resume;
using HrmsAi.ResumeMatch;

var builder = WebApplication.CreateBuilder(args);
builder.AddHrmsAi(new AccessModule(), new ResumeModule(), new JobDescriptionModule(), new GoalsModule(), new MatchModule(), new InvoiceModule(), new ChatModule());

var app = builder.Build();
app.UseHrmsAi();
app.Run();
