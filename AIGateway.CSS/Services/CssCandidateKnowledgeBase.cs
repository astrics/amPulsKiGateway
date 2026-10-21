namespace AiGateway.CSS.Api.Services;

internal static class CssCandidateKnowledgeBase
{
    private static readonly IReadOnlyDictionary<int, string[]> Aliases = new Dictionary<int, string[]>
    {
        [1] = ["kundenfreundlich", "kundenorientiert", "hilfsbereit", "engagiert", "auf beduerfnisse eingegangen", "a l ecoute", "proche des gens", "customer oriented"],
        [2] = ["ehrlich", "glaubwuerdig", "vertrauenswuerdig", "zuverlaessig", "diskret", "serioes", "versprechen eingehalten", "fiable", "fiable et discret", "honnete", "trustworthy", "reliable"],
        [3] = ["freundlich", "freundliches personal", "freundliche mitarbeiter", "nett", "hoeflich", "sympathisch", "aimable", "gentil", "bons contacts", "cordiale", "friendly"],
        [4] = ["kompetent", "professionell", "fachlich", "gut erklaert", "competent", "professionnel", "competente"],
        [5] = ["unkompliziert", "simpel", "einfach geloest", "reibungslos", "simple", "facile", "uncomplicated"],
        [6] = ["praemie", "praemien", "preis", "preise", "teuer", "guenstig", "preis leistung", "prime", "primes", "prix", "cher", "costoso", "prezzo", "price", "prices"],
        [7] = ["praemienrechnung", "zahlungsprozess", "e rechnung", "zahlungsmöglichkeit", "facture de prime", "paiement", "payment process"],
        [8] = ["mahnung", "betreibung", "zahlungserinnerung", "rappel", "poursuite", "sollecito"],
        [9] = ["produktangebot", "produkte", "angebot", "flexibilitaet", "zusatzversicherung", "zusatzversicherungen", "produit", "offre de produits", "assurance complementaire", "product offering"],
        [10] = ["fitnessabo", "gesundheitskonto", "gesundheitsangebot", "praevention", "yoga", "fitness", "prevention"],
        [11] = ["gesundheitsdeklaration", "gesundheitsfragen", "aufnahmebedingung", "abgelehnt", "declaration de sante", "admission"],
        [12] = ["telmed", "callmed", "hmo", "hausarztmodell", "apothekenmodell", "versicherungsmodell", "gewaehltes modell", "modele alternatif", "modele d assurance", "family doctor model"],
        [13] = ["antrag", "offerte", "police", "versicherungspolice", "demande", "offer", "policy"],
        [14] = ["erreichbar", "erreichbarkeit", "warteschleife", "weiterverbunden", "oeffnungszeiten", "hotline", "joignable", "attente au telephone", "reachable"],
        [15] = ["antwortzeit", "schnell", "schnelle bearbeitung", "schnelle abwicklung", "schnelle antwort", "rasch erledigt", "mehrmals nachfragen", "duree de reponse", "reponse rapide", "traitement rapide", "response time", "quick handling"],
        [16] = ["ansprechperson", "persoenlicher kontakt", "agentur vor ort", "immer jemand anderes", "interlocuteur", "contact personnel", "personal contact"],
        [17] = ["nicht geloest", "ungeloest", "keine antwort", "nicht geholfen", "pas resolu", "sans reponse", "not resolved"],
        [18] = ["leistungsuebernahme", "nicht uebernommen", "kosten uebernommen", "bezahlt nicht", "prise en charge", "couverture", "coverage"],
        [19] = ["rueckerstattung dauert", "schnelle rueckerstattung", "rechnungen schnell bearbeitet", "schnelle auszahlung", "remboursement rapide", "paie rapidement", "rimborso rapido", "refund time", "fast reimbursement"],
        [20] = ["leistungsabrechnung verstaendlich", "abrechnung transparent", "decompte clair", "statement of benefits clear"],
        [21] = ["leistungsabrechnung falsch", "falsche abrechnung", "decompte incorrect", "incorrect reimbursement statement"],
        [22] = ["arzt", "therapeut", "medgate", "medi24", "leistungserbringer", "medecin", "therapeute", "doctor"],
        [23] = ["homepage", "webseite", "website", "site internet", "sito web"],
        [24] = ["app", "portal", "online", "online loesung", "online loesungen", "digitale loesung", "digitale loesungen", "app einfach", "portal einfach", "app uebersichtlich", "portal uebersichtlich", "rechnung hochladen", "scanner", "application facile", "solution en ligne", "solutions en ligne", "portail", "app usability", "online solution"],
        [25] = ["app funktioniert nicht", "portal funktioniert nicht", "login problem", "abgestuerzt", "technisches problem", "application plante", "technical problem"],
        [26] = ["image", "serioese versicherung", "guter ruf", "vertrauenswuerdige versicherung", "bonne image", "reputation"],
        [27] = ["sonstiges", "autre", "altro", "other"],
        [28] = ["steuerauszug", "steuerbescheinigung", "attestation fiscale", "tax statement"],
        [29] = ["papierflut", "verwaltungskosten", "buerokratie", "werbung", "administration", "trop de courrier", "bureaucracy"],
        [30] = ["krankenkassen generell", "alle krankenkassen", "einheitskasse", "assurances maladie en general", "health insurers generally"],
        [31] = ["kulant", "kulanz", "grosszuegig", "tolerant", "arrangement commercial", "generous"],
        [32] = ["gut", "super", "ok", "in ordnung", "stimmt alles", "alles toll", "zufrieden", "zufriedenstellend", "allgemein zufrieden", "alles tip top", "langjaehriger kunde", "rundum zufrieden", "globalement satisfait", "tres satisfaite", "overall satisfied"],
        [33] = ["keine erfahrung", "zu wenig erfahrung", "noch nie gebraucht", "erst neu dort", "keine probleme", "pas assez d experience", "new customer", "cannot say"],
        [34] = ["k", "ka", "keine angabe", "k a", "keine antwort", "no answer", "sans indication"],
        [35] = ["unklar", "unbrauchbar", "nicht relevant", "commentaire inutilisable", "unclear comment"],
        [36] = ["rueckforderung einfach", "rueckforderung kompliziert", "remboursement facile", "refund process"],
        [37] = ["gute leistungen", "schlechte leistungen", "prestations bonnes", "benefits generally"],
        [38] = ["guter service", "schlechter service", "kundendienst", "kundenservice", "bon service", "service client", "customer service"]
    };

    private static readonly IReadOnlyDictionary<int, int[]> ConfusableCodes = new Dictionary<int, int[]>
    {
        [1] = [3, 5, 16, 38], [2] = [4, 26], [3] = [1, 38], [4] = [1, 3, 17],
        [5] = [15, 20, 24, 36, 38], [6] = [7, 18, 37], [7] = [6, 20, 29], [8] = [7],
        [9] = [10, 11, 12, 13, 18, 37], [10] = [9, 18, 24], [11] = [9, 13], [12] = [9, 10, 18, 22],
        [13] = [9, 11], [14] = [15, 16, 22, 38], [15] = [14, 17, 19, 38], [16] = [1, 14, 17, 38],
        [17] = [1, 4, 15, 18, 38], [18] = [9, 10, 17, 19, 21, 37], [19] = [15, 18, 20, 36],
        [20] = [7, 18, 19, 21, 36], [21] = [7, 18, 20], [22] = [12, 14, 17, 18],
        [23] = [24, 25], [24] = [5, 10, 20, 23, 25, 36], [25] = [23, 24], [26] = [2, 32],
        [27] = [30, 34, 35], [28] = [7, 29], [29] = [7, 27], [30] = [26, 27, 32],
        [31] = [2, 18, 32], [32] = [26, 33, 35, 38], [33] = [32, 34, 35], [34] = [27, 33, 35],
        [35] = [27, 32, 33, 34], [36] = [5, 19, 20, 24], [37] = [9, 18, 32, 38],
        [38] = [1, 3, 5, 14, 15, 17, 32, 37]
    };

    private static readonly IReadOnlyDictionary<int, string> BoundaryHints = new Dictionary<int, string>
    {
        [1] = "Nicht nur wegen 'freundlich' waehlen (Code 3). Reiner allgemeiner Service ist Code 38.",
        [3] = "Nur Freundlichkeit/Hoeflichkeit. Engagement oder Eingehen auf Beduerfnisse ist Code 1.",
        [5] = "Allgemein unkomplizierte Loesung. App-Bedienung ist Code 24, Rueckforderung Code 36 und Leistungsabrechnung Code 20.",
        [6] = "Preis oder Praemie. Eine Aussage nur zur Leistungsuebernahme ist Code 18.",
        [9] = "Produktangebot, Flexibilitaet oder Produktqualitaet. Leistungen allgemein sind Code 37.",
        [12] = "Nur Telmed, HMO, Hausarzt-, Apotheken- oder aehnliche Versicherungsmodelle. Alternativmedizin ist kein alternatives Versicherungsmodell.",
        [14] = "Erreichbarkeit oder Wartezeit bis zum Kontakt. Dauer bis zur Antwort/Loesung ist Code 15.",
        [15] = "Antwort oder Loesung eines Anliegens. Dauer bis zur Rueckerstattung ist Code 19; telefonische Warteschleife ist Code 14.",
        [17] = "Anliegen ist nicht oder nur teilweise geloest. Nicht mit einer abgelehnten Leistungsuebernahme (Code 18) verwechseln.",
        [18] = "Uebernahme oder Nichtuebernahme einer Leistung. Reine Rueckerstattungsdauer ist Code 19.",
        [19] = "Nur Dauer der Leistungsabwicklung oder Rueckerstattung. Allgemeine Antwortzeit ist Code 15.",
        [20] = "Verstaendlichkeit oder Transparenz der Leistungsabrechnung. Falsche Abrechnung ist Code 21.",
        [23] = "Nur oeffentliche Homepage; App oder Kundenportal sind Codes 24/25.",
        [24] = "Allgemeines Feedback sowie Bedienbarkeit/Funktionen von App oder Portal. Technischer Defekt ist Code 25.",
        [25] = "Technische Stoerung, Login- oder Funktionsfehler. Reine Bedienbarkeit ist Code 24.",
        [27] = "Nur verwenden, wenn kein spezifischerer Code passt.",
        [32] = "Allgemeine Zufriedenheit. 'Keine Erfahrung/kann nichts sagen/keine Probleme' ist Code 33.",
        [33] = "Fehlende oder zu geringe Erfahrung bzw. keine inhaltliche Bewertung. Leere/Platzhalter-Angabe ist Code 34.",
        [34] = "Nur leere oder reine Platzhalter-Angaben. Inhaltlich unklare Kommentare sind Code 35.",
        [35] = "Inhalt vorhanden, aber unbrauchbar oder unklar. Fehlende Erfahrung ist Code 33.",
        [36] = "Einfachheit des Rueckforderungsprozesses. Rueckerstattungsdauer ist Code 19.",
        [37] = "Unspezifische Bewertung von Leistungen. Konkrete Uebernahme/Nichtuebernahme ist Code 18.",
        [38] = "Nur allgemeiner Service/Kundendienst. Konkrete Freundlichkeit ist Code 3, Kundenorientierung Code 1."
    };

    public static IReadOnlyList<string> GetAliases(int codeId) =>
        Aliases.TryGetValue(codeId, out var aliases) ? aliases : Array.Empty<string>();

    public static IReadOnlyList<int> GetConfusableCodeIds(int codeId) =>
        ConfusableCodes.TryGetValue(codeId, out var ids) ? ids : Array.Empty<int>();

    public static string GetBoundaryHint(int codeId) =>
        BoundaryHints.TryGetValue(codeId, out var hint) ? hint : string.Empty;
}
