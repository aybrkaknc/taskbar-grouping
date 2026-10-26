# TaskbarFolders — Benutzerhandbuch

Dieses Handbuch erklärt TaskbarFolders aus Anwendersicht: was das Programm tut, wie Sie es einrichten und was Sie tun können, wenn etwas nicht funktioniert. Vorkenntnisse werden nicht vorausgesetzt.

> Englischsprachige Fassung: [User Guide](user-guide.md). Beide Dokumente beschreiben denselben Stand und werden gemeinsam gepflegt.

## Inhalt

- [Was TaskbarFolders macht](#was-taskbarfolders-macht)
- [Systemvoraussetzungen](#systemvoraussetzungen)
- [Installation](#installation)
- [Schnelleinstieg](#schnelleinstieg)
- [Gruppen anlegen und füllen](#gruppen-anlegen-und-füllen)
- [Das Gruppensymbol](#das-gruppensymbol)
- [An die Taskleiste anheften](#an-die-taskleiste-anheften)
- [Eine Gruppe benutzen](#eine-gruppe-benutzen)
- [Gruppen ändern und löschen](#gruppen-ändern-und-löschen)
- [Einstellungen](#einstellungen)
- [Wo Ihre Daten liegen](#wo-ihre-daten-liegen)
- [Wenn etwas nicht funktioniert](#wenn-etwas-nicht-funktioniert)
- [Was derzeit nicht geht](#was-derzeit-nicht-geht)
- [Deinstallieren](#deinstallieren)

## Was TaskbarFolders macht

TaskbarFolders fasst mehrere Programme zu einer **Gruppe** zusammen und legt für diese Gruppe eine einzelne Kachel in der Windows-Taskleiste an. Ein Klick auf die Kachel klappt die enthaltenen Programme in einem kleinen Fenster auf — vergleichbar mit einem App-Ordner auf dem iPhone-Homescreen.

Der Nutzen: statt acht angehefteter Symbole für Entwicklungswerkzeuge belegt eine Gruppe „Dev Tools" einen Platz, und die acht Programme sind trotzdem mit zwei Klicks erreichbar.

Das Programm besteht aus zwei Teilen:

| Teil | Wofür |
|---|---|
| **TaskbarFolders Manager** | Das Fenster, in dem Sie Gruppen anlegen, Programme hinzufügen und Einstellungen ändern. Das benutzen Sie bewusst. |
| **TaskbarFolders Launcher** | Das kleine Aufklapp-Fenster. Es startet automatisch, wenn Sie auf eine Gruppenkachel klicken, und schließt sich von selbst wieder. |

**Was TaskbarFolders nicht ist:** kein Ersatz für die Taskleiste und kein Eingriff in den Windows-Explorer. Es legt gewöhnliche Verknüpfungen an, mehr nicht. Es ist auch kein Startmenü-Ersatz mit Suche oder Tastenkürzeln — das Aufklapp-Fenster zeigt genau die Programme, die Sie in die Gruppe gelegt haben.

## Systemvoraussetzungen

| | |
|---|---|
| **Betriebssystem** | Windows 11 empfohlen. Der Installer lässt Windows 10 ab Version 1809 zu. |
| **.NET** | Nichts zu installieren. Alles Nötige ist im Programm enthalten. |
| **Architektur** | 64 Bit (x64). Für ARM64 gibt es keine Fassung. |

Zwei Funktionen richten sich nach Ihrer Windows-Version, fallen aber nicht aus, sondern weichen aus:

- **Anheften per Knopfdruck** braucht Windows 10 Version 2004 oder neuer. Wo es nicht verfügbar ist — auch in verwalteten Firmenumgebungen — öffnet TaskbarFolders den Ordner mit der Verknüpfung, damit Sie von Hand anheften können.
- **Der Mica-Hintergrund** des Manager-Fensters braucht Windows 11 22H2 oder neuer. Ältere Versionen zeigen eine schlichte Farbfläche.

## Installation

### Mit Installationsprogramm

1. Laden Sie `TaskbarFolders-Setup.exe` von der [Releases-Seite](https://github.com/gianluca-schwekendiek/taskbar-grouping/releases) herunter.
2. Starten Sie die Datei. Windows fragt nach Administratorrechten — das Programm wird nach „Programme" installiert.
3. Im Assistenten können Sie zwei Dinge wählen:
   - **Mit Windows starten** — ist vorausgewählt. Der Manager startet dann bei jeder Anmeldung mit. Sie können das später in den Einstellungen ändern.
   - **Desktopsymbol anlegen** — ist nicht vorausgewählt.
4. Starten Sie **TaskbarFolders Manager** über das Startmenü.

Eine neue Version installieren Sie einfach über die alte. Der Installer erkennt die vorhandene Installation, ersetzt sie und lässt Ihre Gruppen unangetastet. Schließen Sie vorher den Manager.

### Portable Fassung

1. Laden Sie `TaskbarFolders-portable.zip` herunter und entpacken Sie es an einen beliebigen Ort.
2. Starten Sie `Manager\TaskbarFolders.Manager.exe`.

**Wichtig:** Die beiden Ordner `Manager` und `Launcher` müssen nebeneinander liegen bleiben. Der Manager sucht den Launcher relativ zu sich selbst — trennen Sie die Ordner, lassen sich keine Gruppen mehr erzeugen.

## Schnelleinstieg

1. **TaskbarFolders Manager** öffnen. Der Schreibcursor steht bereits im Namensfeld.
2. Einen Namen tippen, zum Beispiel `Dev Tools`, und auf **+ Add** klicken oder `Enter` drücken.
3. Programme in die Liste ziehen oder über **Add app…** auswählen.
4. Auf **Pin to taskbar** klicken und die Rückfrage von Windows bestätigen.
5. Auf die neue Kachel in der Taskleiste klicken — das Aufklapp-Fenster erscheint.

## Gruppen anlegen und füllen

### Gruppe anlegen

Tippen Sie den Namen in das Feld oben links und klicken Sie auf **+ Add**.

Der Knopf **+ Add** bleibt ausgegraut, solange das Feld leer ist — eine Gruppe braucht einen Namen. Wenn Sie mit der Maus über den ausgegrauten Knopf fahren, erklärt ein Hinweis genau das. `Enter` im Namensfeld hat dieselbe Wirkung wie ein Klick.

In der Liste links stehen alle Gruppen alphabetisch, darunter jeweils die Anzahl der enthaltenen Programme.

### Programme hinzufügen

![Der Manager mit ausgewählter Gruppe](../assets/screenshots/manager-dark.png)

Wählen Sie eine Gruppe aus. Dann entweder

- Dateien mit der Maus aus dem Explorer auf die Programmliste **ziehen**, oder
- auf **Add app…** klicken und eine oder mehrere Dateien auswählen.

Es werden nur Programmdateien (`.exe`) und Verknüpfungen (`.lnk`) übernommen. Ziehen Sie versehentlich anderes mit, wird es kommentarlos übergangen.

Der angezeigte Name stammt aus dem Dateinamen, das Symbol aus der Datei selbst — bei einer Verknüpfung aus dem Programm, auf das sie zeigt.

Der Pfad unter dem Namen wird gekürzt dargestellt: alles innerhalb Ihres Benutzerordners erscheint als `%USERPROFILE%\…`, damit Ihr Kontoname nicht in Screenshots oder bei Bildschirmfreigaben zu sehen ist. Fahren Sie mit der Maus über den Pfad, um ihn vollständig zu sehen. Gespeichert und gestartet wird immer der echte Pfad.

### Programm entfernen

Klicken Sie in der Zeile auf **Remove**. Die Gruppe wird sofort gespeichert und ihr Symbol neu erzeugt.

## Das Gruppensymbol

Das Symbol der Gruppe wird aus den Symbolen der enthaltenen Programme zusammengesetzt. Es verwendet die **ersten vier** Programme der Gruppe — im Aufklapp-Fenster erscheinen später trotzdem alle.

| Programme | Anordnung |
|---|---|
| 1 | Ein Symbol, mittig |
| 2 | Zwei nebeneinander |
| 3 | Zwei oben, eines unten |
| 4 oder mehr | Die ersten vier als 2×2-Raster |

Die Vorschau oben rechts aktualisiert sich kurz nachdem Sie mit dem Ändern fertig sind.

## An die Taskleiste anheften

Eine Gruppe muss mindestens ein Programm enthalten. Eine leere Gruppe erzeugt weder Symbol noch Verknüpfung und lässt sich deshalb nicht anheften.

### Der direkte Weg

Klicken Sie auf **Pin to taskbar**. Windows zeigt daraufhin eine eigene Rückfrage, ob das Anheften erlaubt ist. Bestätigen Sie sie, erscheint die Kachel.

Diese Rückfrage kommt von Windows und lässt sich weder überspringen noch automatisieren. Wenn Sie sie abbrechen, wird nichts angeheftet.

Anschließend prüft der Manager nach, ob tatsächlich eine Kachel entstanden ist, statt sich auf die Rückmeldung von Windows zu verlassen. Sollte der Pin gemeldet werden, ohne dass etwas erscheint, erhalten Sie die Meldung **Pin not confirmed** und der Verknüpfungsordner öffnet sich zum Anheften von Hand. Ihnen wird also nie „angeheftet" gemeldet, wenn nichts angeheftet wurde.

### Wenn der direkte Weg nicht verfügbar ist

Manche Windows-Editionen und viele Firmenrechner verbieten das Anheften durch Programme. In dem Fall sagt der Manager Bescheid und öffnet den Ordner mit der Verknüpfung.

Dort klicken Sie mit der rechten Maustaste auf die `.lnk`-Datei → **Weitere Optionen anzeigen** (unter Windows 11 22H2 und neuer) → **An Taskleiste anheften**.

Diesen Ordner erreichen Sie jederzeit über **Show shortcut…**.

## Eine Gruppe benutzen

Klicken Sie auf die angeheftete Kachel. Neben der Taskleiste öffnet sich ein Fenster mit allen Programmen der Gruppe.

![Das Aufklapp-Fenster: nur Symbole und Namen](../assets/screenshots/popup.png)

- **Auf ein Symbol klicken** startet das Programm, das Fenster schließt sich.
- **Lässt sich ein Programm nicht starten**, bleibt das Fenster offen und zeigt einen roten Hinweis mit dem Namen.
- **Klick daneben** schließt das Fenster.

Das Fenster hat keine Tastaturbedienung: `Esc` schließt es nicht, und beim Öffnen ist kein Symbol vorausgewählt. Klicken Sie daneben, um es zu schließen.

## Gruppen ändern und löschen

Wählen Sie eine Gruppe aus, um Programme hinzuzufügen oder zu entfernen. Jede Änderung wird sofort gespeichert, Symbol und Verknüpfung werden neu erzeugt.

**Löschen:** Rechtsklick auf die Gruppe in der Liste links → **Delete group** → bestätigen. Damit verschwinden Konfiguration, Symbol, Verknüpfung und der Startmenü-Eintrag der Gruppe.

> **Die Kachel in der Taskleiste bleibt dabei bestehen.** Klicken Sie sie mit der rechten Maustaste an und wählen Sie **Von Taskleiste lösen**. Die Sicherheitsabfrage weist Sie darauf hin.

## Einstellungen

Zu erreichen über den **⚙**-Knopf oben rechts.

![Einstellungen](../assets/screenshots/settings.png)

| Einstellung | Auswahl | Standard | Wirkung |
|---|---|---|---|
| Theme | System, Light, Dark | System | *System* übernimmt das Windows-Erscheinungsbild und wechselt sofort mit, wenn Sie es in Windows umstellen. |
| Popup position | Auto, Above, Below | Auto | *Auto* wählt die zur aktuellen Taskleistenposition passende Seite. |
| Enable popup animations | ein / aus | ein | Blendet das Aufklapp-Fenster animiert ein. |
| Start TaskbarFolders Manager when Windows starts | ein / aus | aus | Startet den Manager bei der Anmeldung. |

Änderungen werden erst mit **Save** übernommen. Schließen Sie das Fenster ohne zu speichern, verfallen sie. Solange etwas offen ist, erscheint der Hinweis *Unsaved changes*.

Der Autostart-Haken zeigt den tatsächlichen Zustand in der Windows-Registrierung. Wenn Sie den Eintrag von Hand entfernen, erscheint der Haken beim nächsten Öffnen ausgeschaltet.

### Spaltenanzahl des Aufklapp-Fensters

Jede Gruppe hat einen Wert zwischen 1 und 6 für die Breite des Rasters, standardmäßig 3. Dafür gibt es noch keine Bedienoberfläche — Sie können ihn in der Konfigurationsdatei der Gruppe ändern (siehe nächster Abschnitt), Feld `columns`. Schließen Sie den Manager vorher.

## Wo Ihre Daten liegen

Alles liegt unter `%APPDATA%\TaskbarFolders\`. Diesen Pfad können Sie in die Adressleiste des Explorers einfügen.

| Ordner / Datei | Inhalt |
|---|---|
| `groups\<id>.json` | Eine Datei je Gruppe: Name, enthaltene Programme, Spaltenanzahl |
| `icons\<id>.ico` | Das erzeugte Gruppensymbol |
| `icons\cache\` | Zwischengespeicherte Programmsymbole. Kann gefahrlos gelöscht werden |
| `shortcuts\<id>.lnk` | Die Verknüpfung, die Sie anheften |
| `settings.json` | Ihre Einstellungen |
| `logs\` | Eine Protokolldatei je Tag, zwei Wochen aufbewahrt |

Zusätzlich liegt je Gruppe ein Startmenü-Eintrag unter `%APPDATA%\Microsoft\Windows\Start Menu\Programs\TaskbarFolders\`. Der ist nicht bloß Zierde: Windows merkt sich ein per Knopfdruck gesetztes Anheften nur dann dauerhaft, wenn es die Gruppe im Startmenü kennt. Löschen Sie ihn nicht.

> Der Dateiname bestimmt die Identität einer Gruppe. Benennen Sie `groups\abc.json` um, gilt das als andere Gruppe — Symbol, Verknüpfung und eine bestehende Kachel gehören dann zu einer Gruppe, die es nicht mehr gibt.

## Wenn etwas nicht funktioniert

Der erste Blick geht in die Protokolldateien unter `%APPDATA%\TaskbarFolders\logs\`. Dort steht, warum etwas abgebrochen ist.

| Symptom | Zu prüfen |
|---|---|
| Klick auf die Kachel bewirkt nichts | Heutige `launcher-*.log` öffnen. Beendet sich das Programm mit Code 1, fehlt der Verknüpfung ihre Kennung — Gruppe im Manager erneut anheften. Code 3 bedeutet einen Fehler beim Start, die Ursache steht darunter. |
| Anheften meldet, es sei nicht verfügbar | Ihre Windows-Edition oder Ihre Firmenrichtlinie verbietet es. Über **Show shortcut…** von Hand anheften. |
| Anheften scheint zu klappen, es erscheint aber keine Kachel | Manager schließen und neu öffnen — er repariert dabei die Startmenü-Einträge — und erneut anheften. |
| Kein Symbol und keine Verknüpfung nach dem Hinzufügen | Die Gruppe ist leer, oder von keinem Programm ließ sich ein Symbol lesen. Bei der portablen Fassung prüfen, ob `Manager` und `Launcher` noch nebeneinander liegen. |
| Aufklapp-Fenster erscheint an falscher Stelle | Skalierung der Anzeige, Monitoranordnung und die Bildschirmseite der Taskleiste notieren und melden. |

Ausführlicher, mit Zuordnung von Protokollzeilen zu Ursachen: [Troubleshooting](troubleshooting.md) (englisch).

## Was derzeit nicht geht

Keine Fehler, sondern bewusste oder noch offene Grenzen:

- Gruppen lassen sich **nicht umbenennen**, Programme nicht umsortieren, Symbole nicht selbst wählen, Startparameter nicht setzen.
- Das Aufklapp-Fenster hat **keine Tastaturbedienung**.
- Das Aufklapp-Fenster richtet die Textfarbe nach Ihrem Windows-Erscheinungsbild, nicht nach dem Hintergrundbild. Seit v0.4.7 bekommen die Namen eine Kontur in der Gegenfarbe und bleiben dadurch in jedem Fall lesbar — auf einem gleich hellen Hintergrund wirken sie allerdings umrandet statt gestochen scharf.
- Es gibt **keine ARM64-Fassung**.
- Nichts hindert daran, den Manager **zweimal gleichzeitig** zu starten. Die zweite Instanz kann Änderungen der ersten überschreiben.
- Das **Löschen einer Gruppe entfernt ihre Kachel nicht**.
- Das **Deinstallieren entfernt Ihre Gruppen nicht**.

## Deinstallieren

**Installierte Fassung:** *Einstellungen → Apps → Installierte Apps → TaskbarFolders → Deinstallieren*. Das entfernt das Programm und den Autostart-Eintrag.

**Portable Fassung:** Den entpackten Ordner löschen.

Ihre Gruppen bleiben in beiden Fällen erhalten. Wenn Sie wirklich alles entfernen möchten:

1. Angeheftete Gruppenkacheln mit Rechtsklick von der Taskleiste lösen.
2. Den Ordner `%APPDATA%\TaskbarFolders` löschen.
3. Den Ordner `%APPDATA%\Microsoft\Windows\Start Menu\Programs\TaskbarFolders` löschen.
