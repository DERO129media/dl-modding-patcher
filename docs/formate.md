# Binärformate

## Lokalisierung: `texts/pc/pc_de_lang.binsloc` (in `data_lang/datade.pak`)

Verifiziert und implementiert in `tools/extract_data.py`.

```
Header ... "lockit::LockitBinaryResource" ...
"m_keys"   + 4 Byte (unbekannt) + uint32 count + count × (uint16 len + UTF-8 bytes)
"m_Values" + 4 Byte (unbekannt) + uint32 count + count × (uint16 len + UTF-8 bytes)
```

- Beide Listen haben dieselbe Länge (39.813 Einträge), der Index verbindet Key und Wert.
- **Keys** ohne Beachtung von Groß- und Kleinschreibung vergleichen, z. B. `craft_scrap_n` gegenüber `Craft_Blades_N`.
- **Werte** können auf andere Keys verweisen: `&Throwable_ThrowingKnifeAGen_N&`. Das muss man rekursiv auflösen.
- Namens-Keys: `<Item>_N` oder `<Item>_n` (Name), `<Item>_D` (Beschreibung). Blueprints verweisen per `Name("&…&")` auf ihre Keys.
- Beispiele: `Craft_Blades_N` = „Klingen“, `Craft_Rags_N` = „Stofffetzen“, `Cash_Cash_N` = „Geld aus der alten Welt“, `Medkit_Small_FT_N` = „Verband“.

## Spielstand: `save_ft_0.sav`

Nur untersucht, nicht weiterverfolgt. Wir ändern bewusst **Regeln statt Bestände** (Entscheidung vom 2026-09-27).

- Ort: `<Steam>\userdata\<SteamID>\3008130\remote\out\save\`. Das ist **Steam Cloud**, Änderungen können mit der Cloud kollidieren.
- `save_ft_0.sav` ist gzip-komprimiert (88 KB → 267 KB), dazu gibt es das Backup `save_ft_0_chp000.sbk`.
- Beginnt mit einer Versionskennung `EVersion::DLTB_Patch07_v220`.
- Item-Namen stehen lesbar darin, z. B. kommt `Craft_Blades` 7-mal vor. Wo die Stückzahl steht, ist **unbekannt**. Das ließe sich per Vorher-Nachher-Vergleich herausfinden.
