namespace JobScout.Application.Demo;

/// <summary>
/// The public demo account (ADR-0020): one fictional user anyone can try without signing up. It is searched for
/// every morning like a real user (real current jobs), but visitors can't run the AI or change the CV/letters –
/// they only click through, change statuses and write notes.
/// </summary>
public static class DemoAccount
{
    public const string Provider = "demo";
    public const string Subject = "honza";
    public const string DisplayName = "Honza Testovací";

    public const string CvFileName = "honza-testovaci-cv.md";

    /// <summary>A fictional junior-to-mid .NET developer – narrow on purpose, so the search finds some jobs, not hundreds.</summary>
    public const string Cv = """
        # Honza Testovací
        .NET vývojář · Brno

        ## Shrnutí
        Vývojář se 4 lety praxe v C# a .NET. Celou dobu jsem dělal na jednom produktu – webové aplikaci pro správu
        skladu. Backend v ASP.NET Core, databáze SQL Server, jednoduchý frontend v Razor Pages. Jiné jazyky ani
        technologie jsem v práci nepoužíval.

        ## Praxe
        **.NET vývojář** – Sklady Ukázka s.r.o., Brno (2022 – dosud)
        - Vývoj REST API v ASP.NET Core (.NET 6 → .NET 8), Entity Framework Core, SQL Server
        - Unit testy v xUnit, code review, práce ve Scrumu (tým 5 lidí)
        - Úpravy Razor Pages, opravy chyb, menší optimalizace SQL dotazů

        **Junior C# vývojář** – Ukázkový software a.s., Brno (2021 – 2022)
        - Údržba desktopové aplikace ve WinForms, opravy chyb, jednoduché reporty

        ## Vzdělání
        Bc. Informatika, Vysoké učení technické v Brně (2021)

        ## Dovednosti
        C#, .NET 8, ASP.NET Core, Entity Framework Core, SQL Server, xUnit, Git, Azure DevOps

        ## Jazyky
        Čeština – rodilý mluvčí, angličtina – B1
        """;

    public const string Notes = """
        Hledám práci .NET vývojáře (backend) v Brně, ideálně hybridně – 2 až 3 dny v kanceláři.
        Plný úvazek (HPP), minimálně 65 000 Kč hrubého. Nechci se stěhovat ani dojíždět dál než 30 km.
        Nechci frontend v JavaScriptu ani práci jen na údržbě starého systému.
        """;
}
