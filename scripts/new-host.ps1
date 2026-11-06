param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string[]]$Modules,
    [int]$Port = 5300
)
$ErrorActionPreference = "Stop"
$root = "C:\HRMS AI\app\v2"
$src  = "$root\src"
$catalog = @{
    resume = @{ Project = "HrmsAi.Resume";         Class = "ResumeModule";         Ns = "HrmsAi.Resume" }
    jd     = @{ Project = "HrmsAi.JobDescription"; Class = "JobDescriptionModule"; Ns = "HrmsAi.JobDescription" }
    goals  = @{ Project = "HrmsAi.Goals";          Class = "GoalsModule";          Ns = "HrmsAi.Goals" }
    chat   = @{ Project = "HrmsAi.Chat";           Class = "ChatModule";           Ns = "HrmsAi.Chat" }
    match  = @{ Project = "HrmsAi.ResumeMatch";    Class = "MatchModule";           Ns = "HrmsAi.ResumeMatch" }
}
foreach ($m in $Modules) {
    if (-not $catalog.ContainsKey($m)) { throw "Unknown module '$m'. Available: $($catalog.Keys -join ', ')" }
}
$hostName = "HrmsAi.Host.$Name"
$dir = "$src\$hostName"
if (Test-Path $dir) { throw "$hostName already exists." }
New-Item -ItemType Directory -Force "$dir\Properties" | Out-Null

$refs = @("HrmsAi.Platform", "HrmsAi.Access") + @($Modules | ForEach-Object { $catalog[$_].Project })
$refXml = ($refs | ForEach-Object { "    <ProjectReference Include=`"..\$_\$_.csproj`" />" }) -join "`r`n"
@"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
$refXml
  </ItemGroup>
</Project>
"@ | Set-Content "$dir\$hostName.csproj" -Encoding UTF8

$nsList = @("HrmsAi.Access", "HrmsAi.Platform") + @($Modules | ForEach-Object { $catalog[$_].Ns })
$usings = ($nsList | Sort-Object -Unique | ForEach-Object { "using $_;" }) -join "`r`n"
$ctor   = (@("new AccessModule()") + @($Modules | ForEach-Object { "new $($catalog[$_].Class)()" })) -join ", "
@"
$usings

var builder = WebApplication.CreateBuilder(args);
builder.AddHrmsAi($ctor);

var app = builder.Build();
app.UseHrmsAi();
app.Run();
"@ | Set-Content "$dir\Program.cs" -Encoding UTF8

@"
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:$Port",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
"@ | Set-Content "$dir\Properties\launchSettings.json" -Encoding UTF8

foreach ($f in "appsettings.json", "appsettings.Development.json", "appsettings.Development.json.example") {
    if (Test-Path "$src\HrmsAi.Host.Full\$f") { Copy-Item "$src\HrmsAi.Host.Full\$f" $dir }
}

Push-Location $root
dotnet sln add "$dir\$hostName.csproj" | Out-Null
dotnet build "$dir\$hostName.csproj" -v q
Pop-Location

Write-Host ""
Write-Host "Created $hostName with modules: $($Modules -join ', ')" -ForegroundColor Green
Write-Host "Run it locally on port $Port, or publish it with:  C:\HRMS AI\scripts\publish.bat -Build $Name"

