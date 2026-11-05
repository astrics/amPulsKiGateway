# Kundenabstimmung vom 30.10.2026

Grundlage: im Chat uebermittelte Kundenantwort zu den Metadaten-IDs 6079, 6036 und 5990.

## Fachliche Regeln fuer die neue Testvariante

- 17: Keine Rueckmeldung, keine Antwort, nichts mehr gehoert oder ausdruecklich
  ungeloestes/teilweise geloestes Anliegen. Kein positiver Erledigungs-Code.
- 15 zusaetzlich zu 17 nur bei Dauerbewertung oder wiederholtem Kontakt.
  Positive Bewertungen schneller Antwort/Bearbeitung bleiben entsprechend der
  bestehenden Referenz 15/Positiv; die Kundenabgrenzung wurde auf den Fall
  ausbleibender Rueckmeldungen bezogen. Rueckerstattungsdauer bleibt 19.
- 14/16 brauchen ihren eigenen belegten Aspekt: Erreichbarkeit bzw.
  persoenliche Ansprechperson. Nicht automatisch aus fehlender Antwort ableiten.
- 38 restriktiv fuer allgemeinen, sonst keinem spezifischeren Serviceaspekt
  zuordenbaren Service. Kein automatischer Zusatz zu konkreten Servicecodes.
- 21 betrifft korrekt/falsch; 20 betrifft transparent/verstaendlich/uebersichtlich.
- 32 bei eigenstaendiger allgemeiner Bewertung des Versicherers, auch neben
  konkreten Themen. Keine allgemeine Zufriedenheit aus konkretem Lob erfinden.
  Die vom Kunden angebotene restriktivere Alternative (32 nur allein) ist
  NICHT aktiviert; sie bleibt eine moegliche spaetere Vergleichsvariante.
- Sentiment je Code, bevorzugt Positiv oder Negativ, wenn textlich belegt.
  Neutral bleibt fuer tatsaechlich unklare/unwertende Aspekte moeglich.
  Gemischte Aussage: 32/Positiv und 16/Negativ; nicht beide wegen der Mischung neutral.

## Gesamtsentiment

Der Kunde vergibt kein Gesamtsentiment. Bisherige Vergleiche des gespeicherten
Gesamtwertes mit Projekt 4 sind deshalb KEINE Kunden-Qualitaetskennzahl.
Die PHP-Auswertung kennzeichnet diese Werte jetzt als technische Alt-Aggregate
und bewertet zusaetzlich die Mengen aus Code und zugehoerigem Sentiment.
Fehlendes Code-Sentiment wird nicht aus einem Gesamtwert ergaenzt.

Vorschlag fuer die fachliche Abstimmung: keinen neuen Gesamtwert als Kundenziel
einfuehren. Falls fuer bestehende Schnittstellen benoetigt, den bisherigen
technischen Wert erhalten und in Anzeigen/Exports ausdruecklich als abgeleitet
kennzeichnen. Eine ueberarbeitete Aggregationsregel/Anzeige ist noch abzustimmen.
Der Laufzeit-Parser und die bestehende Aggregation wurden daher nicht geaendert.

## Technische Vorbereitung

`CssCodebook:CustomerRules20261030Projects` aktiviert die Zusatzregeln nur fuer
explizit genannte Projekt-IDs. Standard: keine. Das vorbereitete Profil
`appsettings.CustomerRulesPilot.json` nennt Projekt 10 und candidate_rerank.
Projekt 9 behaelt den bisherigen Prompt bytegleich. Kandidatensuche, Codebuch,
Modell und Klassifikationslogik werden fuer diesen Promptvergleich nicht geaendert.
Die Code-Regeln werden in der neuen Variante ersetzt, damit keine alten,
widersprechenden Abgrenzungen parallel an das Modell gesendet werden.

Projekt 10 wurde noch nicht in MySQL angelegt, der Server nicht deployt und kein
neuer Modelllauf ausgefuehrt. Vor Aktivierung freie Projekt-ID, Modellkennung,
Quantisierung, effektive Tokenlimits und Ergebnisordner mit dem Referenzlauf
abgleichen. Profilnamen ueber DOTNET_ENVIRONMENT/ASPNETCORE_ENVIRONMENT setzen;
der Starter unterstuetzt nun auch `-Profile CustomerRulesPilot`.
Die SQL-Anlage und Bereitstellung sind im PHP-Repository unter
`AI/PROJECT10_CUSTOMER_RULES_TEST.md` beschrieben. Ein Start des Listeners
loest selbst keinen Modelllauf aus.

Pruefung:

```powershell
dotnet run --project Tools/CustomerRulesChecks/CustomerRulesChecks.csproj
```

Neun Pruefungen mit simulierter Modellantwort: alter Prompt unveraendert,
projektbezogener Sync-Aufruf, 32/Positiv und 16/Negativ bleiben erhalten,
bisheriger technischer Gesamtwert bleibt kompatibel. Das prueft die Verdrahtung,
nicht die tatsaechliche Kodierungsqualitaet des Modells.
