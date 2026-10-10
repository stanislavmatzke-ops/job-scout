using JobScout.Application.Admin;
using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Matches;
using JobScout.Application.Profiles;
using JobScout.Application.Reports;
using JobScout.Application.Scouting;
using JobScout.Application.Triage;
using JobScout.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobScout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ProfileExtractor>();
        services.AddSingleton<JobEvaluator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AnalyzeProfile>();
        services.AddScoped<CategoryResolver>();
        services.AddSingleton<CategorySuggester>();
        services.AddScoped<PlanSearch>();
        services.AddScoped<RecordFoundJobs>();
        services.AddScoped<ScoutRunJournal>();
        services.AddScoped<TriageJobs>();
        services.AddScoped<EvaluateJobs>();
        services.AddScoped<WriteCoverLetters>();
        services.AddScoped<UsageReport>();
        services.AddScoped<BuildMorningReport>();
        services.AddScoped<RunScout>();
        services.AddScoped<SignInUser>();
        services.AddScoped<ListMatches>();
        services.AddScoped<GetMatchDetail>();
        services.AddScoped<ChangeMatchStatus>();
        services.AddScoped<UpdateMatchNotes>();
        services.AddScoped<MatchEvaluator>();          // scoped: uses job sources (typed HttpClients)
        services.AddSingleton<CoverLetterWriter>();
        services.AddScoped<EvaluateMatchNow>();
        services.AddScoped<WriteCoverLetterNow>();
        services.AddScoped<UpdateCoverLetter>();
        services.AddScoped<GetProfile>();
        services.AddScoped<UploadCv>();
        services.AddScoped<UpdateProfileNotes>();
        services.AddScoped<UpdateProfileSettings>();
        services.AddScoped<CreateInvitation>();
        services.AddScoped<RequestAccess>();
        services.AddScoped<ListUsers>();
        services.AddScoped<ListInvitations>();
        services.AddScoped<ApproveUser>();
        services.AddScoped<SuspendUser>();
        services.AddScoped<ListScoutUsers>();
        services.AddScoped<PrepareScoutProfile>();
        return services;
    }
}
