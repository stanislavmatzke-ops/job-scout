namespace JobScout.Application.Profiles;

internal static class ProfilePrompts
{
    public const string System = """
        Jsi zkušený IT recruiter. Z CV kandidáta a jeho poznámek vytvoř strukturovaný profil pro porovnávání s nabídkami práce.
        Pravidla:
        - Poznámky kandidáta mají přednost před tím, co se dá odvodit z CV (hlavně preference a priority).
        - Zachyť i nuance preferencí: rozlišuj ideál a to, co je ještě přijatelné.
        - Úroveň jazyků přebírej přesně z CV, neodhaduj ji.
        - Nevymýšlej si: co nelze zjistit, nech prázdné, "unknown" nebo 0.
        - Neuváděj jméno, kontakty ani jiné osobní údaje – profil slouží jen k porovnání s nabídkami.
        - Technologie piš krátce a standardně (např. "C#", ".NET", "Azure", "Oracle PL/SQL").
        - Piš spisovnou češtinou, bez slov v jiných jazycích (kromě názvů technologií).
        - Vyhledávací fráze (searchKeywords) piš tak, jak by je kandidát zadal do vyhledávání na českém pracovním portálu.
        Pokryj cílové role i hlavní technologii, každá fráze má najít jiné nabídky (ne synonyma) a nesmí být příliš obecná (ne jen "vývojář").
        """;

    public const string Schema = """
        {
          "type": "object",
          "properties": {
            "headline":          { "type": "string", "description": "Jedna věta: role, seniorita, hlavní technologie" },
            "yearsOfExperience": { "type": "integer", "description": "Celkové roky praxe v IT" },
            "seniority":         { "type": "string", "enum": ["junior", "medior", "senior", "lead", "architect"] },
            "targetRoles":       { "type": "array", "items": { "type": "string" }, "description": "Pozice, které kandidát hledá (z poznámek, jinak odvozené z CV)" },
            "searchKeywords":    { "type": "array", "items": { "type": "string" }, "description": "3–6 krátkých vyhledávacích frází pro český pracovní portál, od nejdůležitější, česky i anglicky podle toho, jak se pozice inzerují (např. '.NET vývojář', 'softwarový architekt', 'tech lead .NET')" },
            "coreSkills":        { "type": "array", "items": { "type": "string" }, "description": "Hlavní technologie s hlubokou praxí" },
            "otherSkills":       { "type": "array", "items": { "type": "string" }, "description": "Další technologie a znalosti" },
            "domains":           { "type": "array", "items": { "type": "string" }, "description": "Obory, ve kterých kandidát pracoval (pojišťovnictví, státní správa, …)" },
            "languages":         { "type": "array", "items": { "type": "string" }, "description": "Jazyky s úrovní, např. 'angličtina B2'" },
            "preferences": {
              "type": "object",
              "properties": {
                "remoteWork":          { "type": "string", "enum": ["unknown", "full", "hybrid", "onsite"], "description": "Ideální forma práce" },
                "acceptableRemoteWork": { "type": "array", "items": { "type": "string", "enum": ["full", "hybrid", "onsite"] }, "description": "Všechny formy práce, které kandidát ještě přijme (včetně ideální). Např. 'ideálně remote, jinak hybrid' = full + hybrid" },
                "employmentTypes":     { "type": "array", "items": { "type": "string", "enum": ["employee", "contractor"] }, "description": "employee = HPP, contractor = IČO" },
                "minMonthlySalaryCzk": { "type": "integer", "description": "Minimální hrubá měsíční mzda v Kč, 0 pokud neuvedeno" },
                "locations":           { "type": "array", "items": { "type": "string" } },
                "priorities":          { "type": "array", "items": { "type": "string" }, "description": "Co je pro kandidáta důležité, od nejdůležitějšího" },
                "avoid":               { "type": "array", "items": { "type": "string" }, "description": "Čemu se chce kandidát vyhnout" }
              },
              "required": ["remoteWork", "acceptableRemoteWork", "employmentTypes", "minMonthlySalaryCzk", "locations", "priorities", "avoid"],
              "additionalProperties": false
            }
          },
          "required": ["headline", "yearsOfExperience", "seniority", "targetRoles", "coreSkills", "otherSkills", "domains", "languages", "preferences"],
          "additionalProperties": false
        }
        """;
}
