# AI Gateway CSS

Dieses Projekt ist als eigenstaendiges Schwesterprojekt zum bestehenden Sympany-Gateway angelegt.

## Ziel

- separate Ausfuehrung auf derselben Maschine oder spaeter auf einer zweiten Maschine
- eigene Konfiguration, eigene API-Keys, eigener Port und eigene Ergebnisdateien
- Startpunkt fuer das CSS-Codeprojekt mit 35 Codes

## Aktueller Stand

- Codebasis aus dem bestehenden Gateway kopiert
- Namespace und Projektname auf `AiGateway.CSS.Api` umgestellt
- eigener Port `5001`
- eigene Output-Pfade fuer Jobs und Resultate
- eigenes Beispiel-Codebook unter `Codebooks/css-codes.sample.json`
- importiertes Word-Codebook unter `Codebooks/css-codes.imported.json`

## Wichtig

Die eigentliche 35-Code-Logik ist noch nicht eingebaut. Aktuell ist das Projekt technisch getrennt vorbereitet, nutzt aber fachlich noch den bisherigen Prompt-Ansatz. Der naechste sinnvolle Schritt ist die externe Codebook-Anbindung und danach die Anpassung von Prompt und Parsing.

