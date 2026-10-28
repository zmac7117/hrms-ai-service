$resume = @"
John Mathew
Email: john.mathew@example.com | Phone: +91 98765 43210
Senior .NET Developer with 6 years of experience.
Skills: C#, ASP.NET Core, SQL Server, Azure, Docker
Experience:
- ABC Technologies, Senior Developer, 2021-Present
- XYZ Soft, Software Engineer, 2018-2021
Education: B.Tech Computer Science, Calicut University, 2018
"@

$body = @{
  model    = "qwen2.5:3b"
  stream   = $false
  messages = @(
    @{ role = "system"; content = "You extract resume data. Return only JSON matching the schema. Use null if a field is missing. Never invent data." },
    @{ role = "user";   content = $resume }
  )
  options  = @{ temperature = 0 }
  format   = @{
    type = "object"
    properties = @{
      full_name = @{ type = "string" }
      email = @{ type = "string" }
      phone = @{ type = "string" }
      total_experience_years = @{ type = "number" }
      skills = @{ type = "array"; items = @{ type = "string" } }
      experience = @{ type = "array"; items = @{ type = "object"; properties = @{
        company = @{ type = "string" }; title = @{ type = "string" }
        start = @{ type = "string" }; end = @{ type = "string" } } } }
      education = @{ type = "array"; items = @{ type = "object"; properties = @{
        degree = @{ type = "string" }; institution = @{ type = "string" }
        year = @{ type = "string" } } } }
    }
    required = @("full_name","email","skills")
  }
} | ConvertTo-Json -Depth 10

$sw = [Diagnostics.Stopwatch]::StartNew()
$r = Invoke-RestMethod -Uri "http://localhost:11434/api/chat" -Method Post -Body $body -ContentType "application/json"
$sw.Stop()
$r.message.content
"Time: $($sw.Elapsed.TotalSeconds) sec"
