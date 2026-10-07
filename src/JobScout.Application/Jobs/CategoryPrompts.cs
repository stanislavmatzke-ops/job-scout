namespace JobScout.Application.Jobs;

internal static class CategoryPrompts
{
    public const string System = """
                                 Převádíš názvy profesí kandidáta na názvy profesí v katalogu českého pracovního portálu.
                                 Katalog používá krátké obecné názvy v 1. pádě jednotného čísla, např. "Lakýrník", "Zedník",
                                 "Stavbyvedoucí", "Zdravotní sestra", "Skladník", "SW architekt", ".NET vývojář".

                                 Pro každou profesi kandidáta vrať 1–3 názvy, pod kterými ji katalog nejspíš vede, od nejpravděpodobnějšího:
                                 - Když odpovídá profesi ze seznamu ověřených, uveď ji první a přesně tak, jak je v seznamu napsaná.
                                 - Pak případně obecnější název téže práce (např. "Autolakýrník" → "Lakýrník") nebo běžný synonymní název.
                                 - Nikdy nenavrhuj jinou práci ani nižší či pomocnou pozici (ne "Pomocný pracovník", ne "Vedoucí produkce" místo "Vedoucí lakovny").
                                 - Přívlastky jako senior, junior, zkušený vynech; "Vedoucí …" ponech jen když jde o samostatnou profesi.
                                 - Nevymýšlej profese, které s tou kandidátovou nesouvisejí.
                                 - Pole profession opiš přesně tak, jak je v zadání.
                                 """;

    public const string Schema = """
                                 {
                                   "type": "object",
                                   "properties": {
                                     "mappings": {
                                       "type": "array",
                                       "items": {
                                         "type": "object",
                                         "properties": {
                                           "profession": { "type": "string", "description": "Profese kandidáta, opsaná ze zadání" },
                                           "labels":     { "type": "array", "items": { "type": "string" }, "description": "1–3 názvy z katalogu portálu" }
                                         },
                                         "required": ["profession", "labels"],
                                         "additionalProperties": false
                                       }
                                     }
                                   },
                                   "required": ["mappings"],
                                   "additionalProperties": false
                                 }
                                 """;
}