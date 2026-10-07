namespace JobScout.Application.Profiles;

internal static class ProfilePrompts
{
    /// <summary>
    /// Analysis version – part of the input hash. Bump it whenever System or Schema changes:
    /// every stored profile is then re-analysed on its next run (stale profiles never linger).
    /// </summary>
    public const int Version = 2;

    public const string System = """
        Jsi zkušený personalista, který zná český pracovní trh napříč obory – od řemesel, výroby a stavebnictví
        přes služby, obchod a zdravotnictví po kancelářské a IT profese.
        Z CV kandidáta a jeho poznámek vytvoř strukturovaný profil pro vyhledávání a porovnávání nabídek práce.

        Pravidla:
        - Poznámky kandidáta mají přednost před CV (hlavně preference, priority a lokalita).
        - Zachyť nuance preferencí: rozlišuj ideál a to, co je ještě přijatelné.
        - Úroveň jazyků přebírej přesně z CV, neodhaduj ji.
        - Nevymýšlej si: co nelze zjistit, nech prázdné, "unknown" nebo 0.
        - Neuváděj jméno, kontakty ani jiné osobní údaje – profil slouží jen k porovnání s nabídkami.
        - Dovednosti piš krátce, jak se v oboru běžně označují; názvy technologií, materiálů, strojů,
          oprávnění a certifikátů ponech v originále (např. "C#", "Standox", "svářečský průkaz ZK 135", "ŘP sk. C").
        - Údaje si nesmí odporovat: co je v avoid, nesmí být mezi profesemi ani místy pro hledání.
        - Piš spisovnou češtinou (kromě vlastních názvů a názvů technologií).

        Hledání (search):
        - professions: 2–6 názvů pozic, pod kterými se taková práce inzeruje na českých portálech, od nejdůležitější.
          Konkrétně (ne jen "dělník" nebo "vývojář"), každá jiná pozice, ne synonyma téže.
        - keywords: 0–4 fráze, které hledání zúží (dovednost, technologie, materiál) – ne názvy pozic.
        - homeLocation: jedno město nebo obec, odkud kandidát dojíždí (z poznámek, jinak z CV); prázdné, když nejde zjistit.
        - commuteRadiusKm: ochota dojíždět v km od homeLocation. Když kandidát jen vyjmenuje okolní města,
          odhadni okruh, který je pokryje. 0 = neuvedeno.
        - extraLocations: místa mimo okruh, která kandidát výslovně přijímá (např. Praha při hybridní práci).
          Místa přijatelná jen výjimečně sem nedávej – zmiň je v priorities.
        """;

    public const string Schema = """
        {
          "type": "object",
          "properties": {
            "headline":          { "type": "string", "description": "Jedna věta: profese, seniorita a hlavní zaměření" },
            "yearsOfExperience": { "type": "integer", "description": "Celkové roky praxe v oboru" },
            "seniority":         { "type": "string", "enum": ["junior", "medior", "senior", "lead", "expert"],
                                   "description": "junior = začátečník, do ~2 let praxe; medior = samostatný; senior = zkušený, plně samostatný; lead = vede lidi (tým, partu, směnu, provoz); expert = nejvyšší odbornost bez nutnosti vést lidi (architekt, specialista, mistr oboru)" },
            "targetRoles":       { "type": "array", "items": { "type": "string" }, "description": "Pozice, které kandidát hledá (z poznámek, jinak odvozené z CV)" },
            "coreSkills":        { "type": "array", "items": { "type": "string" }, "description": "Hlavní dovednosti s hlubokou praxí (postupy, technologie, stroje, materiály)" },
            "otherSkills":       { "type": "array", "items": { "type": "string" }, "description": "Další dovednosti, znalosti, řidičská a profesní oprávnění, kurzy" },
            "domains":           { "type": "array", "items": { "type": "string" }, "description": "Obory a prostředí, ve kterých kandidát pracoval (autoservis, pozemní stavby, bankovnictví, …)" },
            "languages":         { "type": "array", "items": { "type": "string" }, "description": "Jazyky s úrovní, např. 'angličtina B2'" },
            "preferences": {
              "type": "object",
              "properties": {
                "remoteWork":           { "type": "string", "enum": ["unknown", "full", "hybrid", "onsite"], "description": "Ideální forma práce; u práce, kterou nejde dělat z domova, onsite" },
                "acceptableRemoteWork": { "type": "array", "items": { "type": "string", "enum": ["full", "hybrid", "onsite"] }, "description": "Všechny formy práce, které kandidát ještě přijme (včetně ideální)" },
                "employmentTypes":      { "type": "array", "items": { "type": "string", "enum": ["employee", "contractor"] }, "description": "employee = HPP, contractor = IČO" },
                "minMonthlySalaryCzk":  { "type": "integer", "description": "Minimální hrubá měsíční mzda v Kč, 0 pokud neuvedeno" },
                "priorities":           { "type": "array", "items": { "type": "string" }, "description": "Co je pro kandidáta důležité, od nejdůležitějšího" },
                "avoid":                { "type": "array", "items": { "type": "string" }, "description": "Čemu se chce kandidát vyhnout" }
              },
              "required": ["remoteWork", "acceptableRemoteWork", "employmentTypes", "minMonthlySalaryCzk", "priorities", "avoid"],
              "additionalProperties": false
            },
            "search": {
              "type": "object",
              "properties": {
                "professions":     { "type": "array", "items": { "type": "string" }, "description": "2–6 názvů pozic, jak se inzerují, od nejdůležitější" },
                "keywords":        { "type": "array", "items": { "type": "string" }, "description": "0–4 zužující fráze (dovednost, technologie), ne názvy pozic" },
                "homeLocation":    { "type": "string", "description": "Město/obec, odkud kandidát dojíždí; prázdné pokud neznámé" },
                "commuteRadiusKm": { "type": "integer", "description": "Ochota dojíždět v km, 0 = neuvedeno" },
                "extraLocations":  { "type": "array", "items": { "type": "string" }, "description": "Výslovně přijatelná místa mimo okruh" }
              },
              "required": ["professions", "keywords", "homeLocation", "commuteRadiusKm", "extraLocations"],
              "additionalProperties": false
            }
          },
          "required": ["headline", "yearsOfExperience", "seniority", "targetRoles", "coreSkills", "otherSkills", "domains", "languages", "preferences", "search"],
          "additionalProperties": false
        }
        """;
}