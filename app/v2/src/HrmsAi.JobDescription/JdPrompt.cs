namespace HrmsAi.JobDescription;

// The same instructions your HRMS sends to Groq today, for clients that only send the fields
public static class JdPrompt
{
    public const string System = "You are an HR expert. Provide accurate, professional and concise responses.";

    public static string Build(JdRequest r) => $"""
        Generate professional job content for a role.

        STRICT RULES:
        - Output ONLY valid HTML
        - DO NOT use markdown (no **, ###, -, etc.)
        - Use only <h3>, <p>, <ul>, <li>, <strong> tags
        - No text outside HTML tags
        - Ensure clear spacing between sections using paragraphs
        - Do NOT include a main heading like 'Job Description'

        IMPORTANT:
        - The following information is ONLY for understanding the role
        - DO NOT display, repeat, or reference these details in the output

        ROLE CONTEXT (DO NOT DISPLAY):
        Job Title: {r.JobTitle}
        Position: {r.Position}
        Company: {r.Company}
        Company Description: {r.CompanyDescription}
        Department: {r.Department}
        Sub Department: {r.SubDepartment}
        Grade: {r.Grade}
        Grade Level: {r.GradeLevel}
        Location: {r.Location}
        Employee Type: {r.EmployeeType}
        Job Type: {r.JobType}
        Years of Experience: {r.YearsOfExperience}
        Certifications: {r.Certifications}
        Nationality: {r.Nationality}

        OUTPUT REQUIREMENTS:
        - Start directly with the first section (no overall title)
        - Include ONLY the following sections, in this order:
          1. Job Summary
          2. Key Responsibilities
          3. Required Skills
        - Add a blank paragraph between each section for visual clarity
        """;
}
