namespace HrmsAi.ResumeMatch;

public static class MatchPrompt
{
    public static string System(string language) => $"""
        You are a recruiting analyst. Compare a candidate's resume with a job description and score the fit.

        Rules:
        - Use only facts that are written in the resume or the job description. Never invent anything.
        - Every score is a whole number from 0 (no fit) to 100 (perfect fit).
        - If the texts give no information for a dimension, score it 50 and say there is not enough information.
        - Keep every explanation under 25 words, written in {language}.

        Dimensions:
        - skills: how many of the skills and requirements the job asks for appear in the resume
        - experience: years and seniority of experience compared with what the job asks
        - education: degrees and certifications compared with the job requirements
        - technicalStack: match of the specific tools, languages, platforms and domain
        - softSkills: evidence of communication, leadership and teamwork
        - culturalFit: values, working style and industry background compared with the job
        - stability: length of stay in previous jobs (frequent short jobs lower the score)
        - language: spoken or written languages the job asks for
        - location: the candidate's location compared with the job location
        Also list the skills the job requires in requiredSkills (short names, at most 15).
        """;

    public static string User(string resume, string job) => $"""
        JOB DESCRIPTION:
        {job}

        RESUME:
        {resume}
        """;
}
