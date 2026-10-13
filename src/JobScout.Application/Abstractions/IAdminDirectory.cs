namespace JobScout.Application.Abstractions;

/// <summary>
/// Who is an admin. Admins come from the host's configuration, never from the database (ADR-0017);
/// the use cases only ask – e.g. admins have no AI limits (ADR-0021).
/// </summary>
public interface IAdminDirectory
{
    bool IsAdmin(string provider, string subject);
}
