using JobScout.Core;

namespace JobScout.Application.Evaluation;

public sealed record JobEvaluation(
    int Score,
    string Summary,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Questions,
    string Salary,
    RemoteWork RemoteWork,
    EmploymentType EmploymentType);
