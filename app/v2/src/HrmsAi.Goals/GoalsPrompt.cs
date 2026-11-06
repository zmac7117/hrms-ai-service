namespace HrmsAi.Goals;

// The same instructions your HRMS sends to Groq today, for clients that only send the fields
public static class GoalsPrompt
{
    public const string System =
        "You are an expert HR Performance Management consultant. You specialize in creating SMART performance goals " +
        "that help employees achieve career growth while contributing to organizational success. Always respond with valid JSON only.";

    public static string Build(GoalRequest r)
    {
        var count = r.GoalCount is > 0 ? r.GoalCount.Value : 5;
        return $$"""
            As an HR Performance Management Expert, generate SMART (Specific, Measurable, Achievable, Relevant, Time-bound) performance goals based on the employee's career aspirations and current role.

            EMPLOYEE INFORMATION:
            Career Goal Description: {{r.SummaryDescription}}
            Current Position: {{r.Position}}
            Department: {{r.Department}}
            Sub Department: {{r.SubDepartment}}
            Grade: {{r.Grade}}
            Grade Level: {{r.GradeLevel}}
            Employee Type: {{r.EmployeeType}}

            OUTPUT FORMAT REQUIREMENTS:
            1. Generate EXACTLY {{count}} performance goals that bridge the gap between the current role and the career aspirations
            2. Each goal must be SMART
            3. Each goal should have 2-3 measurable KPIs (Key Performance Indicators)
            4. Output MUST be a valid JSON array only, no other text
            5. Use this exact JSON structure for each goal object:
               { "goal": "Goal title here", "kpis": ["KPI 1", "KPI 2", "KPI 3"] }

            KPI GUIDELINES:
            - Start with action verbs (Complete, Achieve, Reduce, Increase, Implement, etc.)
            - Include measurable metrics (%, numbers, dates)
            - Be time-bound
            - Make it realistic but challenging

            EXAMPLE:
            [
              { "goal": "Improve Financial Accuracy and Compliance", "kpis": ["Maintain at least 99% accuracy in financial entries and reports", "Complete monthly reconciliations before deadline (100% on-time rate)"] },
              { "goal": "Enhance Reporting Efficiency", "kpis": ["Submit monthly financial reports within 3 working days", "Reduce report preparation time by 15%"] }
            ]

            OUTPUT ONLY THE JSON ARRAY, NO OTHER TEXT:
            """;
    }
}
