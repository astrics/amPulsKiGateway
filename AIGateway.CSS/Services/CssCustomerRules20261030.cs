using System.Text;

namespace AiGateway.CSS.Api.Services;

internal static class CssCustomerRules20261030
{
    public static string? FollowUpRule(int id) => id switch
    {
        6 => "Bei blosser Nennung von Preis oder Preis-Leistungs-Verhaeltnis das Sentiment aus KUZU ableiten: 1-4 Negativ, 5-7 Neutral, 8-10 Positiv. Eine ausdrueckliche Preisbewertung hat Vorrang. KUZU bestimmt keine anderen Themen.",
        9 => "Verstaendlichkeit, Uebersichtlichkeit und Qualitaet des Produktangebots; auch eine Bewertung des Gesamtpakets, etwa 'Fuer unsere Familie aktuell das beste Gesamtpaket' => 9/Positiv.",
        15 => "Auch unspezifische Zeitbewertungen ohne benannten Vorgang kodieren: 'Schnelligkeit' => 15/Positiv; 'manchmal etwas langatmig' => 15/Negativ. Explizite Rueckerstattungsdauer bleibt bei 19.",
        33 => "Ausdruecklich fehlende oder geringe Erfahrung als eigenen Aspekt kodieren, auch neben einer verwertbaren Bewertung mit Code 32. Ein Wechsel allein belegt noch keine geringe Erfahrung. Ohne eigene Bewertung der Erfahrung ist das Sentiment fuer 33 Neutral.",
        38 => "Allgemeinen Kundendienst als eigenen Aspekt kodieren, auch bei blosser Nennung und neben getrennten Themen. 'Kundenservice, App' => 38/Neutral und 24/Neutral. Ausdrueckliche Bewertungen bestimmen das jeweilige Sentiment.",
        _ => null
    };

    public static string? FollowUpBoundary(int id) => id switch
    {
        6 => "Bei reiner Preisnennung KUZU fuer das Sentiment nutzen; explizite Wertung hat Vorrang.",
        9 => "Bewertung des Gesamtpakets ist Code 9; daraus keine weiteren allgemeinen Codes 32 oder 37 ableiten.",
        15 => "Ein nicht genannter Vorgang schliesst 15 bei ausdruecklicher Zeitbewertung nicht aus. Rueckerstattung ist 19.",
        33 => "Eine vorhandene Bewertung schliesst 33 bei ausdruecklich geringer Erfahrung nicht aus.",
        38 => "Eine eigenstaendige allgemeine Kundenservice-Nennung ist auch ohne Wertung 38/Neutral. Einen bereits spezifisch kodierten Serviceaspekt nicht nochmals als 38 kodieren.",
        _ => null
    };

    public static void AppendFollowUpInstructions(StringBuilder builder)
    {
        builder.AppendLine("Ergaenzung zur Kundenabstimmung:");
        builder.AppendLine("- Kundenrueckmeldung 2026-11-24: Reine Preisnennung mit KUZU bewerten (1-4 Negativ, 5-7 Neutral, 8-10 Positiv); explizite Preiswertung hat Vorrang. 'Preis,Leistung' bei KUZU 9 => 6/Positiv; 'Preis-Leistungs-Verhaeltnis, App' bei KUZU 8 => 6/Positiv und 24/Neutral.");
        builder.AppendLine("- 'Schnelligkeit' => 15/Positiv; 'manchmal etwas langatmig' => 15/Negativ, auch ohne konkret benannten Vorgang.");
        builder.AppendLine("- 'Fuer unsere Familie aktuell das beste Gesamtpaket.' => 9/Positiv.");
        builder.AppendLine("- 'Kundenservice, App' => 38/Neutral und 24/Neutral.");
        builder.AppendLine("- Eigenstaendige allgemeine Bewertung des Kundendiensts auch neben getrennten Themen als 38 kodieren, etwa 'Der Kundenservice ist prima. Das Portal ist auch gut' => 38/Positiv und 24/Positiv.");
        builder.AppendLine("- Ausdruecklich geringe Erfahrung auch neben einer Bewertung kodieren, etwa 'Bisher sehr zufrieden, fuer ein Urteil habe ich aber noch zu wenig Erfahrung' => 32/Positiv und 33/Neutral.");
        builder.AppendLine();
    }

    public static string? CodingRule(int id) => id switch
    {
        15 => "Bewertung der Antwort- oder Loesungsdauer (z.B. schnell, zu lange, 14 Tage sind zu lang) oder wiederholte Kontaktaufnahme. Bei einer bloss ausbleibenden Antwort ohne Dauerbewertung nur Code 17. Rueckerstattungsdauer gehoert zu 19.",
        17 => "Anliegen noch nicht oder nur teilweise geloest; auch 'keine Rueckmeldung', 'keine Antwort', 'nichts mehr gehoert'. Nicht wegen schneller Bearbeitung, reibungslosem Ablauf oder ausdruecklich fehlender Probleme waehlen.",
        20 => "Aussage ueber Verstaendlichkeit, Uebersichtlichkeit oder Transparenz der Leistungsabrechnung. Eine lediglich korrekte oder falsche Abrechnung gehoert zu 21.",
        21 => "Korrektheit oder Fehler der Leistungsabrechnung bzw. Abwicklung, einschliesslich ausdruecklich korrekter Abrechnungen.",
        32 => "Eigenstaendige allgemeine Bewertung des Krankenversicherers, z.B. 'macht ihren Job gut' oder 'soweit alles in Ordnung'. Auch neben konkreten Aspekten waehlen, wenn diese allgemeine Bewertung ausdruecklich vorliegt. Nicht aus einem konkreten Lob oder einer konkreten Beschwerde zusaetzlich ableiten.",
        38 => "Restriktiver Auffangcode fuer ausdruecklich allgemeinen Service, der keinem spezifischeren Serviceaspekt zugeordnet werden kann. Nicht zusaetzlich zu bereits konkret beschriebenen Serviceaspekten vergeben.",
        _ => null
    };

    public static string? Boundary(int id) => id switch
    {
        14 => "Konkrete Erreichbarkeit, Warteschleife, Oeffnungszeiten oder Weiterverbinden. Eine bloss fehlende Rueckmeldung allein belegt diesen Code nicht; dafuer Code 17.",
        15 => "Keine automatische Zusatzkodierung zu 17: Es braucht eine Dauerbewertung oder wiederholte Kontaktaufnahme. Ausbleibende Antwort allein ist 17. Rueckerstattungsdauer ist 19.",
        16 => "Persoenliche Ansprechperson oder Kontinuitaet muss ausdruecklich Thema sein. Fehlende Antwort allein belegt keinen fehlenden persoenlichen Ansprechpartner.",
        17 => "Keine positive Umdeutung zu 'Anliegen geloest'. Schnelle Bearbeitung, reibungsloser Ablauf und 'nie Probleme' rechtfertigen 17 nicht. Eine reine Leistungsablehnung bleibt 18, sofern kein weiteres ungeloestes Anliegen beschrieben ist.",
        20 => "Korrektheit ist 21; korrekt bedeutet nicht automatisch transparent oder verstaendlich.",
        21 => "Korrekt oder falsch betrifft 21. Transparent oder verstaendlich betrifft 20; beide nur bei getrennt belegten Aspekten.",
        32 => "Explizite allgemeine Bewertung darf neben konkreten Codes stehen. Keine zusaetzliche allgemeine Zufriedenheit aus ausschliesslich konkreten Aspekten erfinden.",
        38 => "Nur fuer nicht spezifischer zuordenbaren allgemeinen Service. Nicht automatisch wegen 'Kundendienst' oder als Zusatz zu Antwortzeit, persoenlichem Kontakt, Freundlichkeit oder ungeloestem Anliegen vergeben.",
        _ => null
    };

    public static void AppendInstructions(StringBuilder builder)
    {
        builder.AppendLine("Kundenabstimmung 2026-10-30 (verbindlich fuer diese Testvariante):");
        builder.AppendLine("- Vergib fuer JEDEN ausgewaehlten Code sein eigenes Sentiment zum jeweiligen Textaspekt. Positive und negative Codes koennen in derselben Aussage vorkommen.");
        builder.AppendLine("- Moeglichst Positiv oder Negativ anhand des belegten Aspekts vergeben. Neutral nur, wenn fuer diesen Aspekt keine eindeutige Wertung vorliegt; keine Wertung erfinden.");
        builder.AppendLine("- Kein Gesamt-Sentiment ausgeben und nicht auf alle Codes uebertragen. Gemischte Aspekte machen die einzelnen Codes nicht automatisch Neutral.");
        builder.AppendLine("- 'Keine Rueckmeldung auf wichtige Fragen' => 17/Negativ; ohne Dauerbewertung keine 15, ohne persoenliche Ansprechperson keine 16, kein allgemeiner Zusatzcode 38.");
        builder.AppendLine("- 'Seit 14 Tagen keine Antwort, das ist zu lange' => 17/Negativ und 15/Negativ.");
        builder.AppendLine("- 'Schnelle Antworten, unkompliziert' => 15/Positiv und 5/Positiv; nicht 17 und keine unbelegten Zusatzcodes.");
        builder.AppendLine("- 'Die Versicherung macht ihren Job gut; die Abrechnungen sind korrekt' => 32/Positiv und 21/Positiv; nicht 17 oder 20.");
        builder.AppendLine("- 'Soweit alles in Ordnung. Der persoenliche Kontakt fehlt.' => 32/Positiv und 16/Negativ; nicht 15, 17 oder 38.");
        builder.AppendLine();
    }
}
