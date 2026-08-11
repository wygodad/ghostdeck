namespace GhostDeck;

/// <summary>Prosta lokalizacja. Kolejnosc jezykow = indeksy w tablicach.</summary>
public static class Lang
{
    // indeks: 0 en, 1 pl, 2 de, 3 fr, 4 es, 5 zh, 6 pt, 7 ru
    public static readonly string[] Codes = { "en", "pl", "de", "fr", "es", "zh", "pt", "ru" };
    public static readonly string[] Names = { "English", "Polski", "Deutsch", "Français", "Español", "中文", "Português (BR)", "Русский" };

    private static int _idx = 0;

    public static string CurrentCode => Codes[_idx];

    public static void Set(string code)
    {
        int i = Array.IndexOf(Codes, code);
        _idx = i >= 0 ? i : 0;
    }

    public static string T(string key)
    {
        if (Map.TryGetValue(key, out var arr))
            return _idx < arr.Length && !string.IsNullOrEmpty(arr[_idx]) ? arr[_idx] : arr[0];
        return key;
    }

    // The translation table used to be ONE collection initializer with 500+ entries.
    // A method that large costs a surprising amount to JIT at startup (measured: 13.6 ms
    // and ~16 MB of private bytes for 71 KB of actual strings), which a tray app pays on
    // every launch. The entries are unchanged and still one per line, all 8 languages
    // together - they are just filled in by a handful of smaller methods.
    // tools/lang-check.py verifies on every push that each key still has all 8.
    private static readonly Dictionary<string, string[]> Map = Build();

    private static Dictionary<string, string[]> Build()
    {
        var m = new Dictionary<string, string[]>(584);
        L00(m);
        L01(m);
        L02(m);
        L03(m);
        L04(m);
        L05(m);
        L06(m);
        L07(m);
        L08(m);
        L09(m);
        L10(m);
        L11(m);
        L12(m);
        return m;
    }

    private static void L00(Dictionary<string, string[]> m)
    {
        // ---- sub-tabs ----
        m["subtab_start"]    = new[] { "Start", "Start", "Start", "Accueil", "Inicio", "开始", "Início", "Начало" };
        m["subtab_profiles"] = new[] { "Profiles", "Profile", "Profile", "Profils", "Perfiles", "配置文件", "Perfis", "Профили" };
        m["subtab_curve"]    = new[] { "Fan curve", "Krzywa wentylatora", "Lüfterkurve", "Courbe du ventilateur", "Curva del ventilador", "风扇曲线", "Curva da ventoinha", "Кривая вентилятора" };
        m["st_sub_charts"]   = new[] { "Charts", "Wykresy", "Diagramme", "Graphiques", "Gráficos", "图表", "Gráficos", "Графики" };
        m["st_sub_bytes"]    = new[] { "EC bytes", "Bajty EC", "EC-Bytes", "Octets EC", "Bytes EC", "EC 字节", "Bytes EC", "Байты EC" };
        m["st_sub_log"]      = new[] { "Change log", "Historia zmian", "Änderungen", "Journal", "Registro", "更改记录", "Alterações", "Журнал" };

        // ---- report: fan-curve verification flow ----
        m["rep_curve_intro"]   = new[]
        {
            "Help verify the fan-curve addresses for your model. Set the exact test curve below in MSI Center, then capture — we read it back from the EC (read-only) and locate it, then prepare a GitHub report.",
            "Pomóż zweryfikować adresy krzywej wentylatora dla Twojego modelu. Ustaw w MSI Center dokładnie poniższą krzywą testową, potem kliknij Zbierz — odczytamy ją z EC (tylko odczyt), zlokalizujemy i przygotujemy zgłoszenie na GitHub.",
            "Hilf, die Lüfterkurven-Adressen für dein Modell zu verifizieren. Stelle in MSI Center genau die untenstehende Testkurve ein, dann erfasse — wir lesen sie aus dem EC zurück (nur Lesen), lokalisieren sie und erstellen einen GitHub-Bericht.",
            "Aidez à vérifier les adresses de la courbe de ventilation pour votre modèle. Réglez exactement la courbe de test ci-dessous dans MSI Center, puis capturez — nous la relisons depuis l'EC (lecture seule), la localisons et préparons un rapport GitHub.",
            "Ayuda a verificar las direcciones de la curva del ventilador de tu modelo. Configura en MSI Center exactamente la curva de prueba de abajo y luego captura — la leemos del EC (solo lectura), la localizamos y preparamos un informe de GitHub.",
            "帮助验证你机型的风扇曲线地址。在 MSI Center 中精确设置下面的测试曲线，然后采集——我们会从 EC 读回（只读）并定位它，随后生成 GitHub 报告。",
            "Ajude a verificar os endereços da curva da ventoinha do seu modelo. Defina no MSI Center exatamente a curva de teste abaixo e capture — lemos de volta do EC (somente leitura), localizamos e preparamos um relatório no GitHub.",
            "Помогите проверить адреса кривой вентилятора для вашей модели. Задайте в MSI Center точно указанную ниже тестовую кривую, затем снимите — мы считаем её из EC (только чтение), найдём и подготовим отчёт на GitHub."
        };
        m["rep_curve_warn"] = new[]
        {
            "MSI Center only lets you edit the fan curve in Extreme Performance. Switch to that profile first.",
            "MSI Center pozwala edytować krzywą tylko w trybie Extreme Performance. Najpierw przełącz się na ten profil.",
            "MSI Center erlaubt das Bearbeiten der Lüfterkurve nur im Modus Extreme Performance. Wechsle zuerst zu diesem Profil.",
            "MSI Center ne permet de modifier la courbe qu'en mode Extreme Performance. Passez d'abord à ce profil.",
            "MSI Center solo permite editar la curva en modo Extreme Performance. Cambia primero a ese perfil.",
            "MSI Center 仅允许在 Extreme Performance 模式下编辑风扇曲线。请先切换到该配置文件。",
            "O MSI Center só permite editar a curva no modo Extreme Performance. Mude primeiro para esse perfil.",
            "MSI Center позволяет редактировать кривую только в режиме Extreme Performance. Сначала переключитесь на этот профиль."
        };
        m["rep_curve_steps"] = new[] { "STEPS", "KROKI", "SCHRITTE", "ÉTAPES", "PASOS", "步骤", "PASSOS", "ШАГИ" };
        m["rep_curve_why"] = new[]
        {
            "The values are deliberately unusual, so we can find exactly where MSI Center wrote them in the EC and confirm the curve addresses for your model.",
            "Wartości są celowo nietypowe, żebyśmy mogli znaleźć dokładnie tam, gdzie MSI Center zapisał je w EC, i potwierdzić adresy krzywej dla Twojego modelu.",
            "Die Werte sind bewusst ungewöhnlich, damit wir genau finden, wohin MSI Center sie im EC geschrieben hat, und die Kurvenadressen für dein Modell bestätigen können.",
            "Les valeurs sont volontairement inhabituelles, afin de trouver exactement où MSI Center les a écrites dans l'EC et de confirmer les adresses de la courbe pour votre modèle.",
            "Los valores son deliberadamente inusuales para poder encontrar exactamente dónde los escribió MSI Center en el EC y confirmar las direcciones de la curva de tu modelo.",
            "这些数值刻意与众不同，便于我们准确找到 MSI Center 在 EC 中写入的位置，并确认你机型的曲线地址。",
            "Os valores são propositalmente incomuns, para encontrarmos exatamente onde o MSI Center os gravou no EC e confirmar os endereços da curva do seu modelo.",
            "Значения намеренно необычные, чтобы мы могли точно найти, куда MSI Center записал их в EC, и подтвердить адреса кривой для вашей модели."
        };
        m["rep_curve_s1"] = new[]
        {
            "Switch the laptop to the Extreme Performance profile (Features → Extreme Performance).",
            "Przełącz laptop na profil Extreme Performance (Features → Extreme Performance).",
            "Wechsle das Notebook in das Profil Extreme Performance (Features → Extreme Performance).",
            "Passez le PC au profil Extreme Performance (Features → Extreme Performance).",
            "Cambia el portátil al perfil Extreme Performance (Features → Extreme Performance).",
            "将笔记本切换到 Extreme Performance 配置文件（Features → Extreme Performance）。",
            "Mude o notebook para o perfil Extreme Performance (Features → Extreme Performance).",
            "Переключите ноутбук в профиль Extreme Performance (Features → Extreme Performance)."
        };
        m["rep_curve_s2"] = new[]
        {
            "Open Advanced (the gear icon) → the Fan Speed tab → Advanced mode.",
            "Otwórz Advanced (koło zębate) → zakładka Fan Speed → tryb Advanced.",
            "Öffne Advanced (Zahnrad) → Reiter Fan Speed → Modus Advanced.",
            "Ouvrez Advanced (l'engrenage) → onglet Fan Speed → mode Advanced.",
            "Abre Advanced (el engranaje) → pestaña Fan Speed → modo Advanced.",
            "打开 Advanced（齿轮图标）→ Fan Speed 选项卡 → Advanced 模式。",
            "Abra Advanced (a engrenagem) → aba Fan Speed → modo Advanced.",
            "Откройте Advanced (шестерёнка) → вкладка Fan Speed → режим Advanced."
        };
        m["rep_curve_s3"] = new[]
        {
            "Set Fan 1 (CPU) to these values, in order: 25, 35, 45, 55, 65, 75 %.",
            "Ustaw wentylator 1 (CPU) na kolejne wartości: 25, 35, 45, 55, 65, 75 %.",
            "Stelle Lüfter 1 (CPU) der Reihe nach auf: 25, 35, 45, 55, 65, 75 %.",
            "Réglez le ventilateur 1 (CPU) sur, dans l'ordre : 25, 35, 45, 55, 65, 75 %.",
            "Ajusta el ventilador 1 (CPU) a, en orden: 25, 35, 45, 55, 65, 75 %.",
            "将风扇 1（CPU）依次设为：25、35、45、55、65、75 %。",
            "Defina o ventilador 1 (CPU), em ordem: 25, 35, 45, 55, 65, 75 %.",
            "Задайте вентилятор 1 (CPU) по порядку: 25, 35, 45, 55, 65, 75 %."
        };
        m["rep_curve_s4"] = new[]
        {
            "Set Fan 2 (GPU) to these values, in order: 20, 30, 40, 50, 60, 70 %.",
            "Ustaw wentylator 2 (GPU) na kolejne wartości: 20, 30, 40, 50, 60, 70 %.",
            "Stelle Lüfter 2 (GPU) der Reihe nach auf: 20, 30, 40, 50, 60, 70 %.",
            "Réglez le ventilateur 2 (GPU) sur, dans l'ordre : 20, 30, 40, 50, 60, 70 %.",
            "Ajusta el ventilador 2 (GPU) a, en orden: 20, 30, 40, 50, 60, 70 %.",
            "将风扇 2（GPU）依次设为：20、30、40、50、60、70 %。",
            "Defina o ventilador 2 (GPU), em ordem: 20, 30, 40, 50, 60, 70 %.",
            "Задайте вентилятор 2 (GPU) по порядку: 20, 30, 40, 50, 60, 70 %."
        };
        m["rep_curve_s5"] = new[]
        {
            "Click Save in MSI Center, come back here and press “Capture & scan”.",
            "Kliknij Save w MSI Center, wróć tutaj i naciśnij „Zbierz i skanuj”.",
            "Klicke in MSI Center auf Save, komm hierher zurück und drücke „Erfassen und scannen“.",
            "Cliquez sur Save dans MSI Center, revenez ici et appuyez sur « Capturer et analyser ».",
            "Haz clic en Save en MSI Center, vuelve aquí y pulsa «Capturar y escanear».",
            "在 MSI Center 点击 Save，返回此处并点击“采集并扫描”。",
            "Clique em Save no MSI Center, volte aqui e pressione “Capturar e analisar”.",
            "Нажмите Save в MSI Center, вернитесь сюда и нажмите «Снять и просканировать»."
        };
        m["rep_curve_capture"] = new[] { "Capture & scan", "Zbierz i skanuj", "Erfassen und scannen", "Capturer et analyser", "Capturar y escanear", "采集并扫描", "Capturar e analisar", "Снять и просканировать" };
        m["rep_curve_finish"]  = new[] { "Open GitHub report", "Otwórz zgłoszenie GitHub", "GitHub-Bericht öffnen", "Ouvrir le rapport GitHub", "Abrir informe de GitHub", "打开 GitHub 报告", "Abrir relatório no GitHub", "Открыть отчёт на GitHub" };
        m["rep_curve_found"]   = new[]
        {
            "Test curve found — CPU @ 0x{0:X2}, GPU @ 0x{1:X2}.", "Znaleziono krzywą testową — CPU @ 0x{0:X2}, GPU @ 0x{1:X2}.",
            "Testkurve gefunden — CPU @ 0x{0:X2}, GPU @ 0x{1:X2}.", "Courbe de test trouvée — CPU @ 0x{0:X2}, GPU @ 0x{1:X2}.",
            "Curva de prueba encontrada — CPU @ 0x{0:X2}, GPU @ 0x{1:X2}.", "已找到测试曲线 — CPU @ 0x{0:X2}，GPU @ 0x{1:X2}。",
            "Curva de teste encontrada — CPU @ 0x{0:X2}, GPU @ 0x{1:X2}.", "Тестовая кривая найдена — CPU @ 0x{0:X2}, GPU @ 0x{1:X2}."
        };
        m["rep_curve_match"]   = new[]
        {
            "Matches the shipped map — this model's curve can be marked verified.", "Zgodne z mapą w aplikacji — krzywą tego modelu można oznaczyć jako zweryfikowaną.",
            "Stimmt mit der mitgelieferten Zuordnung überein — die Kurve dieses Modells kann als verifiziert markiert werden.", "Correspond à la carte fournie — la courbe de ce modèle peut être marquée comme vérifiée.",
            "Coincide con el mapa incluido — la curva de este modelo puede marcarse como verificada.", "与内置映射一致——该机型的曲线可标记为已验证。",
            "Corresponde ao mapa incluído — a curva deste modelo pode ser marcada como verificada.", "Совпадает со встроенной картой — кривую этой модели можно отметить как проверенную."
        };
        m["rep_curve_nomatch"] = new[]
        {
            "Differs from the shipped map — sending the addresses for review.", "Różni się od mapy w aplikacji — wysyłam adresy do przeglądu.",
            "Weicht von der mitgelieferten Zuordnung ab — Adressen werden zur Prüfung gesendet.", "Diffère de la carte fournie — envoi des adresses pour examen.",
            "Difiere del mapa incluido — enviando las direcciones para revisión.", "与内置映射不同——正在发送地址以供审核。",
            "Difere do mapa incluído — enviando os endereços para revisão.", "Отличается от встроенной карты — отправляю адреса на проверку."
        };
        m["rep_curve_notfound"]= new[]
        {
            "Couldn't locate the test curve. Send the dump anyway so we can map it (did you Save in MSI Center?).", "Nie udało się znaleźć krzywej testowej. Wyślij zrzut mimo to (czy na pewno kliknięto Save w MSI Center?).",
            "Testkurve nicht gefunden. Sende den Dump trotzdem, damit wir sie zuordnen können (in MSI Center gespeichert?).", "Impossible de localiser la courbe de test. Envoyez quand même le vidage pour qu'on la cartographie (avez-vous cliqué sur Save dans MSI Center ?).",
            "No se pudo localizar la curva de prueba. Envía el volcado de todos modos para mapearla (¿guardaste en MSI Center?).", "未能定位测试曲线。仍请发送转储以便我们映射（是否在 MSI Center 点击了 Save？）。",
            "Não foi possível localizar a curva de teste. Envie o despejo mesmo assim para mapearmos (você clicou em Save no MSI Center?).", "Не удалось найти тестовую кривую. Всё равно отправьте дамп, чтобы мы её сопоставили (нажали Save в MSI Center?)."
        };
        m["rep_curve_cpuonly"] = new[]
        {
            "CPU test curve found at 0x{0:X2}; no GPU test curve in the dump - single-fan model (one slider in MSI Center) or Fan 2 not set.",
            "Znaleziono krzywą testową CPU pod 0x{0:X2}; brak krzywej GPU w zrzucie - model z jednym wentylatorem (jeden suwak w MSI Center) albo Fan 2 nie został ustawiony.",
            "CPU-Testkurve bei 0x{0:X2} gefunden; keine GPU-Testkurve im Dump - Modell mit einem Lüfter (ein Regler in MSI Center) oder Fan 2 nicht gesetzt.",
            "Courbe de test CPU trouvée à 0x{0:X2} ; pas de courbe GPU dans le vidage - modèle à un seul ventilateur (un seul curseur dans MSI Center) ou Fan 2 non réglé.",
            "Curva de prueba de CPU encontrada en 0x{0:X2}; sin curva de GPU en el volcado - modelo de un solo ventilador (un deslizador en MSI Center) o Fan 2 sin configurar.",
            "在 0x{0:X2} 找到 CPU 测试曲线；转储中没有 GPU 测试曲线 - 单风扇机型（MSI Center 只有一个滑块）或未设置风扇 2。",
            "Curva de teste da CPU encontrada em 0x{0:X2}; sem curva de GPU no despejo - modelo com uma ventoinha (um controle no MSI Center) ou Fan 2 não definido.",
            "Тестовая кривая CPU найдена по адресу 0x{0:X2}; кривой GPU в дампе нет - модель с одним вентилятором (один ползунок в MSI Center) или Fan 2 не задан."
        };
        m["rep_curve_gpuonly"] = new[]
        {
            "GPU test curve found at 0x{0:X2}; the CPU test curve was not found in the dump.",
            "Znaleziono krzywą testową GPU pod 0x{0:X2}; krzywej testowej CPU nie znaleziono w zrzucie.",
            "GPU-Testkurve bei 0x{0:X2} gefunden; die CPU-Testkurve wurde im Dump nicht gefunden.",
            "Courbe de test GPU trouvée à 0x{0:X2} ; la courbe de test CPU n'a pas été trouvée dans le vidage.",
            "Curva de prueba de GPU encontrada en 0x{0:X2}; la curva de prueba de CPU no se encontró en el volcado.",
            "在 0x{0:X2} 找到 GPU 测试曲线；转储中未找到 CPU 测试曲线。",
            "Curva de teste da GPU encontrada em 0x{0:X2}; a curva de teste da CPU não foi encontrada no despejo.",
            "Тестовая кривая GPU найдена по адресу 0x{0:X2}; тестовая кривая CPU в дампе не найдена."
        };
        m["rep_curve_notadvanced"]= new[]
        {
            "The Advanced fan curve isn't active right now — your laptop is in another profile, so the EC still holds the default curve. Switch to Extreme Performance, set the Advanced curve in MSI Center, click Save, and stay in Extreme, then capture again.",
            "Zaawansowana krzywa nie jest teraz aktywna — laptop jest w innym profilu, więc w EC jest wciąż domyślna krzywa. Przełącz na Extreme Performance, ustaw krzywą (Advanced) w MSI Center, kliknij Save i zostań w Extreme, potem przechwyć ponownie.",
            "Die erweiterte Lüfterkurve ist gerade nicht aktiv — dein Notebook ist in einem anderen Profil, daher enthält der EC noch die Standardkurve. Wechsle zu Extreme Performance, stelle die Advanced-Kurve in MSI Center ein, klicke Save, bleib in Extreme und erfasse erneut.",
            "La courbe avancée n'est pas active actuellement — votre PC est sur un autre profil, donc l'EC contient encore la courbe par défaut. Passez en Extreme Performance, réglez la courbe (Advanced) dans MSI Center, cliquez sur Save, restez en Extreme, puis capturez à nouveau.",
            "La curva avanzada no está activa ahora — tu portátil está en otro perfil, así que el EC aún tiene la curva por defecto. Cambia a Extreme Performance, configura la curva (Advanced) en MSI Center, haz clic en Save, quédate en Extreme y captura de nuevo.",
            "高级风扇曲线当前未激活——你的笔记本处于其他配置文件，因此 EC 中仍是默认曲线。请切换到 Extreme Performance，在 MSI Center 设置 Advanced 曲线，点击 Save 并保持在 Extreme，然后重新采集。",
            "A curva avançada não está ativa agora — seu notebook está em outro perfil, então o EC ainda tem a curva padrão. Mude para Extreme Performance, defina a curva (Advanced) no MSI Center, clique em Save, permaneça no Extreme e capture novamente.",
            "Расширенная кривая сейчас не активна — ноутбук в другом профиле, поэтому в EC всё ещё стандартная кривая. Переключитесь на Extreme Performance, задайте кривую (Advanced) в MSI Center, нажмите Save, оставайтесь в Extreme и снимите ещё раз."
        };

        // ---- Models: verify CTA ----
        m["models_verify_desc"]  = new[]
        {
            "Experimental models work but aren't hardware-confirmed. A 2-minute read-only capture lets us promote your model to Tested.",
            "Modele eksperymentalne działają, ale nie są potwierdzone sprzętowo. 2-minutowy odczyt (bez zapisu) pozwala awansować Twój model do Tested.",
            "Experimentelle Modelle funktionieren, sind aber nicht per Hardware bestätigt. Eine 2-minütige Nur-Lesen-Erfassung befördert dein Modell zu „Getestet“.",
            "Les modèles expérimentaux fonctionnent mais ne sont pas confirmés matériellement. Une capture en lecture seule de 2 min permet de faire passer votre modèle en « Testé ».",
            "Los modelos experimentales funcionan pero no están confirmados por hardware. Una captura de solo lectura de 2 minutos permite promover tu modelo a Probado.",
            "实验性机型可用，但未经硬件确认。2 分钟的只读采集即可将你的机型提升为“已测试”。",
            "Modelos experimentais funcionam, mas não são confirmados por hardware. Uma captura somente-leitura de 2 minutos permite promover seu modelo para Testado.",
            "Экспериментальные модели работают, но не подтверждены на железе. Двухминутное снятие (только чтение) позволит повысить вашу модель до «Проверено»."
        };
        m["models_verify_btn"]   = new[] { "Verify my model", "Zweryfikuj mój model", "Modell verifizieren", "Vérifier mon modèle", "Verificar mi modelo", "验证我的机型", "Verificar meu modelo", "Проверить мою модель" };

        // ---- Fan curve: report button ----
        m["fc_report_curve"] = new[] { "Report fan curve", "Zgłoś krzywą", "Lüfterkurve melden", "Signaler la courbe", "Reportar la curva", "报告风扇曲线", "Reportar a curva", "Сообщить о кривой" };

        // ---- tray: grouped report submenu ----
        m["tray_report"]       = new[] { "Report / verify", "Zgłoś / zweryfikuj", "Melden / verifizieren", "Signaler / vérifier", "Reportar / verificar", "报告 / 验证", "Reportar / verificar", "Сообщить / проверить" };
        m["tray_report_model"] = new[] { "My model…", "Mój model…", "Mein Modell…", "Mon modèle…", "Mi modelo…", "我的机型…", "Meu modelo…", "Моя модель…" };
        m["tray_report_curve"] = new[] { "Fan curve…", "Krzywą wentylatora…", "Lüfterkurve…", "Courbe du ventilateur…", "Curva del ventilador…", "风扇曲线…", "Curva da ventoinha…", "Кривая вентилятора…" };

        m["menu_settings"]  = new[] { "Settings", "Ustawienia", "Einstellungen", "Paramètres", "Configuración", "设置", "Configurações", "Настройки" };
        m["menu_status"]    = new[] { "Status", "Status", "Status", "Statut", "Estado", "状态", "Status", "Состояние" };
        m["menu_panel"]     = new[] { "Open panel", "Otwórz panel", "Panel öffnen", "Ouvrir le panneau", "Abrir panel", "打开面板", "Abrir painel", "Открыть панель" };

        // ---- Fan Boost (max fans) — generic name; the MSI "Cooler Boost" trademark is only referenced once, in the README ----
        m["cooler_boost"]     = new[] { "Fan Boost (max fans)", "Fan Boost (maks. wentylatory)", "Fan Boost (max. Lüfter)", "Fan Boost (ventilo max)", "Fan Boost (ventiladores máx.)", "Fan Boost（风扇全速）", "Fan Boost (ventoinhas máx.)", "Fan Boost (макс. вентиляторы)" };
        m["cooler_boost_on"]  = new[] { "Max fans ON", "Maks. obroty WŁ.", "Max. Lüfter EIN", "Ventilo max ACTIVÉ", "Ventiladores máx. ACT.", "风扇全速 开", "Ventoinhas máx. LIG.", "Макс. обороты ВКЛ" };
        m["cooler_boost_off"] = new[] { "Max fans off", "Maks. obroty WYŁ.", "Max. Lüfter AUS", "Ventilo max désactivé", "Ventiladores máx. des.", "风扇全速 关", "Ventoinhas máx. DESL.", "Макс. обороты ВЫКЛ" };
        m["cooler_boost_hint"]= new[] { "Force full fan speed regardless of profile. When turned off, the fans spin down gradually (can take 10–25 s).", "Wymuś pełne obroty wentylatorów niezależnie od profilu. Po wyłączeniu wentylatory zwalniają stopniowo (może to potrwać 10–25 s).", "Volle Lüfterdrehzahl unabhängig vom Profil erzwingen. Nach dem Ausschalten drehen die Lüfter allmählich herunter (kann 10–25 s dauern).", "Forcer la vitesse max des ventilateurs quel que soit le profil. À l'arrêt, les ventilateurs ralentissent progressivement (10–25 s).", "Forzar ventiladores al máximo sin importar el perfil. Al desactivar, bajan de forma gradual (puede tardar 10–25 s).", "无视配置文件强制风扇全速。关闭后风扇会逐渐降速（约 10–25 秒）。", "Forçar ventoinhas no máximo independentemente do perfil. Ao desligar, desaceleram gradualmente (pode levar 10–25 s).", "Принудительно макс. обороты независимо от профиля. При выключении вентиляторы снижают обороты постепенно (10–25 с)." };
        m["cooler_boost_short"]= new[] { "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost" };
        m["scen_features"]    = new[] { "Features", "Funkcje", "Funktionen", "Fonctions", "Funciones", "功能", "Funções", "Функции" };

        // ---- gaming overlay ----
        m["overlay_title"]    = new[] { "Gaming overlay", "Nakładka do gier", "Gaming-Overlay", "Overlay de jeu", "Overlay de juego", "游戏悬浮窗", "Overlay de jogo", "Игровой оверлей" };
        m["overlay_hint"]     = new[] { "Detachable always-on-top mini panel with temps, fan RPM and profile — for gaming. Drag to move; toggle with the hotkey. Options in Settings.", "Odczepiany, zawsze-na-wierzchu mini panel z temperaturami, obrotami i profilem — do grania. Przeciągnij, by przesunąć; przełącz skrótem. Opcje w Ustawieniach.", "Abnehmbares Mini-Panel (immer im Vordergrund) mit Temperaturen, Lüfter-RPM und Profil — fürs Gaming. Zum Verschieben ziehen; per Hotkey umschalten. Optionen in den Einstellungen.", "Mini-panneau détachable toujours au premier plan (températures, RPM, profil) — pour le jeu. Glisser pour déplacer ; raccourci pour afficher. Options dans les Réglages.", "Mini panel desacoplable siempre visible con temperaturas, RPM y perfil — para juegos. Arrastra para mover; alterna con el atajo. Opciones en Ajustes.", "可拆卸的置顶小面板，显示温度、风扇转速和配置文件——为游戏而生。拖动移动；快捷键切换。选项在设置中。", "Mini-painel destacável sempre no topo com temperaturas, RPM e perfil — para jogos. Arraste para mover; alterne com o atalho. Opções nas Definições.", "Открепляемая мини-панель поверх окон: температуры, обороты, профиль — для игр. Перетаскивайте мышью; переключение горячей клавишей. Настройки в разделе Настройки." };
        m["set_grp_overlay"]  = new[] { "Gaming overlay", "Nakładka do gier", "Gaming-Overlay", "Overlay de jeu", "Overlay de juego", "游戏悬浮窗", "Overlay de jogo", "Игровой оверлей" };
        m["ov_show"]          = new[] { "Show overlay", "Pokaż nakładkę", "Overlay anzeigen", "Afficher l'overlay", "Mostrar overlay", "显示悬浮窗", "Mostrar overlay", "Показать оверлей" };
        m["ov_layout"]        = new[] { "Layout", "Układ", "Layout", "Disposition", "Diseño", "布局", "Layout", "Вид" };
        m["ov_layout_card"]   = new[] { "Card", "Karta", "Karte", "Carte", "Tarjeta", "卡片", "Cartão", "Карта" };
        m["ov_layout_bar"]    = new[] { "Bar", "Pasek", "Leiste", "Barre", "Barra", "条形", "Barra", "Панель" };
    }

    private static void L01(Dictionary<string, string[]> m)
    {
        m["ov_opacity"]       = new[] { "Content opacity", "Przezroczystość treści", "Deckkraft Inhalt", "Opacité du contenu", "Opacidad contenido", "内容不透明度", "Opacidade do conteúdo", "Прозрачность содержимого" };
        m["ov_bg_opacity"]    = new[] { "Background opacity", "Przezroczystość tła", "Deckkraft Hintergrund", "Opacité du fond", "Opacidad de fondo", "背景不透明度", "Opacidade do fundo", "Прозрачность фона" };
        m["ov_scale"]         = new[] { "Size", "Rozmiar", "Größe", "Taille", "Tamaño", "大小", "Tamanho", "Размер" };
        m["ov_clickthrough"]  = new[] { "Lock position (click-through, can't drag)", "Zablokuj pozycję (klik-through, bez przeciągania)", "Position sperren (klick-durchlässig, kein Ziehen)", "Verrouiller (clic traversant, non déplaçable)", "Bloquear posición (clic pasante, sin arrastrar)", "锁定位置（点击穿透，不可拖动）", "Bloquear posição (clique-através, sem arrastar)", "Заблокировать (сквозные клики, без перетаскивания)" };
        m["ov_lock_menu"]     = new[] { "Lock overlay position", "Zablokuj pozycję nakładki", "Overlay-Position sperren", "Verrouiller l'overlay", "Bloquear posición del overlay", "锁定悬浮窗位置", "Bloquear posição do overlay", "Заблокировать оверлей" };
        m["ov_locked"]        = new[] { "Locked · click-through", "Zablokowana · klik-through", "Gesperrt · klick-durchlässig", "Verrouillé · clic traversant", "Bloqueado · clic pasante", "已锁定 · 点击穿透", "Bloqueado · clique-através", "Заблокирована · сквозные клики" };
        m["ov_unlocked"]      = new[] { "Unlocked · drag to move", "Odblokowana · przeciągnij", "Entsperrt · zum Verschieben ziehen", "Déverrouillé · glisser pour déplacer", "Desbloqueado · arrastra para mover", "已解锁 · 拖动移动", "Desbloqueado · arraste para mover", "Разблокирована · перетащите" };
        m["ov_ontop"]         = new[] { "Always on top", "Zawsze na wierzchu", "Immer im Vordergrund", "Toujours au premier plan", "Siempre visible", "总在最前", "Sempre no topo", "Поверх всех окон" };
        m["ov_accent"]        = new[] { "Accent = profile colour", "Akcent = kolor profilu", "Akzent = Profilfarbe", "Accent = couleur du profil", "Acento = color del perfil", "强调色 = 配置文件颜色", "Destaque = cor do perfil", "Акцент = цвет профиля" };
        m["ov_bold"]          = new[] { "Bold text", "Pogrubiony tekst", "Fetter Text", "Texte en gras", "Texto en negrita", "粗体文字", "Texto em negrito", "Жирный текст" };
        m["ov_metrics"]       = new[] { "What to show", "Co pokazywać", "Was anzeigen", "Quoi afficher", "Qué mostrar", "显示内容", "O que mostrar", "Что показывать" };
        m["ov_position"]      = new[] { "Position", "Pozycja", "Position", "Position", "Posición", "位置", "Posição", "Позиция" };
        m["ov_options"]       = new[] { "Options", "Opcje", "Optionen", "Options", "Opciones", "选项", "Opções", "Опции" };
        m["ov_hotkey"]        = new[] { "Shortcut — show/hide", "Skrót — pokaż/ukryj", "Kürzel — ein/aus", "Raccourci — afficher/masquer", "Atajo — mostrar/ocultar", "快捷键 — 显示/隐藏", "Atalho — mostrar/ocultar", "Клавиша — показать/скрыть" };
        m["ov_drag_hint"]     = new[] { "or drag with the mouse", "lub przeciągnij myszą", "oder mit der Maus ziehen", "ou glisser à la souris", "o arrastra con el ratón", "或用鼠标拖动", "ou arraste com o rato", "или перетащите мышью" };
        m["ov_pos_pick"]      = new[] { "Corners…", "Rogi…", "Ecken…", "Coins…", "Esquinas…", "边角…", "Cantos…", "Углы…" };
        m["ov_pos_tl"]        = new[] { "↖ Top-left", "↖ Lewy górny", "↖ Oben links", "↖ Haut gauche", "↖ Sup. izq.", "↖ 左上", "↖ Sup. esq.", "↖ Сверху слева" };
        m["ov_pos_tr"]        = new[] { "↗ Top-right", "↗ Prawy górny", "↗ Oben rechts", "↗ Haut droite", "↗ Sup. der.", "↗ 右上", "↗ Sup. dir.", "↗ Сверху справа" };
        m["ov_pos_bl"]        = new[] { "↙ Bottom-left", "↙ Lewy dolny", "↙ Unten links", "↙ Bas gauche", "↙ Inf. izq.", "↙ 左下", "↙ Inf. esq.", "↙ Снизу слева" };
        m["ov_pos_br"]        = new[] { "↘ Bottom-right", "↘ Prawy dolny", "↘ Unten rechts", "↘ Bas droite", "↘ Inf. der.", "↘ 右下", "↘ Inf. dir.", "↘ Снизу справа" };
        m["ov_m_temp"]        = new[] { "CPU / GPU temp", "Temp. CPU / GPU", "CPU-/GPU-Temp.", "Temp. CPU / GPU", "Temp. CPU / GPU", "CPU / GPU 温度", "Temp. CPU / GPU", "Темп. CPU / GPU" };
        m["ov_m_rpm"]         = new[] { "Fan RPM", "Obroty (RPM)", "Lüfter-RPM", "RPM ventilos", "RPM ventilador", "风扇转速", "RPM ventoinha", "Обороты" };
        m["ov_m_fanpct"]      = new[] { "Fan %", "Wentylatory (%)", "Lüfter %", "Ventilo %", "Ventilador %", "风扇 %", "Ventoinha %", "Вентил. %" };
        m["ov_m_profile"]     = new[] { "Active profile", "Aktywny profil", "Aktives Profil", "Profil actif", "Perfil activo", "当前配置文件", "Perfil ativo", "Активный профиль" };
        m["ov_m_cooler"]      = new[] { "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost", "Fan Boost" };
        m["ov_m_load"]        = new[] { "CPU load", "Obciążenie CPU", "CPU-Last", "Charge CPU", "Carga CPU", "CPU 占用", "Carga CPU", "Загрузка CPU" };
        m["ov_m_ram"]         = new[] { "RAM", "RAM", "RAM", "RAM", "RAM", "内存", "RAM", "ОЗУ" };
        m["ov_m_gpuusage"]    = new[] { "GPU load", "Użycie GPU", "GPU-Last", "Charge GPU", "Carga GPU", "GPU 占用", "Carga GPU", "Загрузка GPU" };
        m["ov_m_vram"]        = new[] { "VRAM", "VRAM", "VRAM", "VRAM", "VRAM", "显存", "VRAM", "Видеопамять" };
        m["ov_m_cpuclock"]    = new[] { "CPU clock", "Zegar CPU", "CPU-Takt", "Fréq. CPU", "Reloj CPU", "CPU 频率", "Clock CPU", "Частота CPU" };
        m["ov_bg"]            = new[] { "Background", "Tło", "Hintergrund", "Arrière-plan", "Fondo", "背景", "Fundo", "Фон" };
        m["ov_bg_color"]      = new[] { "Background colour", "Kolor tła", "Hintergrundfarbe", "Couleur de fond", "Color de fondo", "背景颜色", "Cor de fundo", "Цвет фона" };
        m["ov_restore"]       = new[] { "Restore defaults", "Przywróć domyślne", "Standard wiederherstellen", "Réinitialiser", "Restaurar valores", "恢复默认", "Restaurar padrões", "Сбросить" };
        m["ov_m_charge"]      = new[] { "Charge limit", "Limit ładowania", "Ladelimit", "Limite de charge", "Límite de carga", "充电限制", "Limite de carga", "Лимит заряда" };
        m["ov_m_battery"]     = new[] { "Battery %", "Bateria %", "Akku %", "Batterie %", "Batería %", "电量 %", "Bateria %", "Батарея %" };
        m["ov_lock_row"]      = new[] { "Lock position", "Zablokuj pozycję", "Position sperren", "Verrouiller la position", "Bloquear posición", "锁定位置", "Bloquear posição", "Заблокировать позицию" };
        m["ov_note"]          = new[] { "Shows over borderless / windowed-fullscreen games. Exclusive fullscreen may hide it.", "Widoczna w grach borderless / windowed-fullscreen. Tryb exclusive fullscreen może ją ukryć.", "Sichtbar bei randlosen / Fenster-Vollbild-Spielen. Exklusives Vollbild kann es verdecken.", "Visible sur les jeux sans bordure / plein écran fenêtré. Le plein écran exclusif peut le masquer.", "Visible en juegos sin bordes / pantalla completa en ventana. La pantalla completa exclusiva puede ocultarlo.", "在无边框/窗口化全屏游戏中可见。独占全屏可能会隐藏它。", "Visível em jogos sem bordas / ecrã inteiro em janela. O ecrã inteiro exclusivo pode ocultá-lo.", "Виден в играх без рамки / оконный полноэкранный. Эксклюзивный полноэкранный может скрыть его." };

        // ---- history log ----
        m["menu_log"]         = new[] { "Change log", "Historia zmian", "Änderungsprotokoll", "Journal des changements", "Registro de cambios", "更改日志", "Registro de alterações", "Журнал изменений" };
        m["log_title"]        = new[] { "Change history", "Historia zmian", "Änderungsverlauf", "Historique des changements", "Historial de cambios", "更改历史", "Histórico de alterações", "История изменений" };
        m["log_recent"]       = new[] { "Recent changes", "Ostatnie zmiany", "Letzte Änderungen", "Changements récents", "Cambios recientes", "最近更改", "Alterações recentes", "Недавние изменения" };
        m["log_full"]         = new[] { "Full log…", "Pełny log…", "Vollständiges Protokoll…", "Journal complet…", "Registro completo…", "完整日志…", "Registro completo…", "Полный журнал…" };
        m["log_empty"]        = new[] { "No changes recorded yet", "Brak zapisanych zmian", "Noch keine Änderungen", "Aucun changement enregistré", "Sin cambios registrados", "尚无记录", "Nenhuma alteração registrada", "Изменений пока нет" };
        m["log_col_time"]     = new[] { "Time", "Czas", "Zeit", "Heure", "Hora", "时间", "Hora", "Время" };
        m["log_col_source"]   = new[] { "Source", "Źródło", "Quelle", "Source", "Origen", "来源", "Origem", "Источник" };
        m["log_col_detail"]   = new[] { "Written bytes", "Zapisane bajty", "Geschriebene Bytes", "Octets écrits", "Bytes escritos", "写入字节", "Bytes escritos", "Записанные байты" };
    }

    private static void L02(Dictionary<string, string[]> m)
    {
        m["log_col_result"]   = new[] { "Readback", "Odczyt", "Rücklesen", "Relecture", "Relectura", "回读", "Releitura", "Обратное чтение" };
        m["log_copy_all"]     = new[] { "Copy all", "Kopiuj wszystko", "Alles kopieren", "Tout copier", "Copiar todo", "全部复制", "Copiar tudo", "Копировать всё" };
        m["log_clear"]        = new[] { "Clear", "Wyczyść", "Löschen", "Effacer", "Borrar", "清除", "Limpar", "Очистить" };
        m["log_clear_confirm"]= new[] { "Clear the whole change history?", "Wyczyścić całą historię zmian?", "Gesamten Änderungsverlauf löschen?", "Effacer tout l'historique ?", "¿Borrar todo el historial?", "清除全部历史？", "Limpar todo o histórico?", "Очистить всю историю?" };
        m["log_read_fail"]    = new[] { "readback failed", "odczyt nieudany", "Rücklesen fehlgeschlagen", "relecture échouée", "relectura fallida", "回读失败", "releitura falhou", "ошибка чтения" };
        m["log_err"]          = new[] { "error", "błąd", "Fehler", "erreur", "error", "错误", "erro", "ошибка" };
        m["log_charge"]       = new[] { "Charge limit {0}%", "Limit ładowania {0}%", "Ladelimit {0}%", "Limite de charge {0}%", "Límite de carga {0}%", "充电限制 {0}%", "Limite de carga {0}%", "Лимит заряда {0}%" };
        m["log_external"]     = new[] { "External change: {0} → {1}", "Zmiana zewnętrzna: {0} → {1}", "Externe Änderung: {0} → {1}", "Changement externe : {0} → {1}", "Cambio externo: {0} → {1}", "外部更改：{0} → {1}", "Alteração externa: {0} → {1}", "Внешнее изменение: {0} → {1}" };
        m["log_curve_on"]     = new[] { "Custom fan curve ON", "Własna krzywa wentylatora WŁ.", "Eigene Lüfterkurve EIN", "Courbe perso ACTIVÉE", "Curva personalizada ACT.", "自定义风扇曲线 开", "Curva personalizada LIG.", "Своя кривая ВКЛ" };
        m["log_curve_off"]    = new[] { "Custom fan curve off", "Własna krzywa wentylatora WYŁ.", "Eigene Lüfterkurve AUS", "Courbe perso désactivée", "Curva personalizada des.", "自定义风扇曲线 关", "Curva personalizada DESL.", "Своя кривая ВЫКЛ" };

        // ---- log sources ----
        m["log_src_startup"]  = new[] { "startup", "start", "Start", "démarrage", "inicio", "启动", "início", "запуск" };
        m["log_src_hotkey"]   = new[] { "hotkey", "skrót", "Tastenkürzel", "raccourci", "atajo", "快捷键", "atalho", "гор. клавиша" };
        m["log_src_tray"]     = new[] { "tray", "zasobnik", "Infobereich", "barre d'état", "bandeja", "托盘", "bandeja", "трей" };
        m["log_src_panel"]    = new[] { "panel", "panel", "Panel", "panneau", "panel", "面板", "painel", "панель" };
        m["log_src_autoac"]   = new[] { "auto AC/battery", "auto AC/bateria", "Auto Netz/Akku", "auto secteur/batt.", "auto CA/batería", "自动 电源/电池", "auto CA/bateria", "авто сеть/батарея" };
        m["log_src_fancurve"] = new[] { "fan curve", "krzywa went.", "Lüfterkurve", "courbe ventilo", "curva vent.", "风扇曲线", "curva ventoinha", "кривая вент." };
        m["log_src_external"] = new[] { "external sync", "sync. zewn.", "externer Sync", "sync externe", "sinc. externa", "外部同步", "sinc. externa", "внешняя синхр." };
        m["log_src_charge"]   = new[] { "charge limit", "limit ładowania", "Ladelimit", "limite charge", "límite carga", "充电限制", "limite carga", "лимит заряда" };
        m["log_src_cooler"]   = new[] { "fan boost", "fan boost", "Fan Boost", "fan boost", "fan boost", "Fan Boost", "fan boost", "fan boost" };
        m["log_src_firmware"] = new[] { "firmware", "firmware", "Firmware", "firmware", "firmware", "固件", "firmware", "прошивка" };
        m["log_src_test"]     = new[] { "test tool", "narz. testowe", "Testtool", "outil de test", "herr. de prueba", "测试工具", "ferr. de teste", "тест. инстр." };
        m["log_src_thermal"]  = new[] { "thermal", "temperatura", "thermisch", "thermique", "térmico", "温度", "térmico", "температура" };

        // ---- thermal alert (Settings -> Notifications) ----
        m["set_grp_alerts"]  = new[] { "Notifications", "Powiadomienia", "Benachrichtigungen", "Notifications", "Notificaciones", "通知", "Notificações", "Уведомления" };
        m["ta_enable"]       = new[] { "Temperature alert", "Alert temperatury", "Temperaturwarnung", "Alerte de température", "Alerta de temperatura", "温度警报", "Alerta de temperatura", "Оповещение о температуре" };
        m["ta_threshold"]    = new[] { "Threshold", "Próg", "Schwelle", "Seuil", "Umbral", "阈值", "Limite", "Порог" };
        m["ta_time"]         = new[] { "For at least", "Przez co najmniej", "Für mindestens", "Pendant au moins", "Durante al menos", "持续至少", "Por pelo menos", "Не менее" };
        m["ta_alert_title"]  = new[] { "High temperature", "Wysoka temperatura", "Hohe Temperatur", "Température élevée", "Temperatura alta", "温度过高", "Temperatura alta", "Высокая температура" };
        m["set_osd_secs"]    = new[] { "OSD display time", "Czas wyświetlania OSD", "OSD-Anzeigedauer", "Durée d'affichage de l'OSD", "Duración del OSD", "OSD 显示时长", "Duração do OSD", "Время показа OSD" };

        // ---- generic dialog buttons ----
        m["gen_ok"]     = new[] { "OK", "OK", "OK", "OK", "Aceptar", "确定", "OK", "ОК" };
        m["gen_cancel"] = new[] { "Cancel", "Anuluj", "Abbrechen", "Annuler", "Cancelar", "取消", "Cancelar", "Отмена" };

        // ---- fan-curve presets ----
        m["fc_preset"]      = new[] { "Preset", "Preset", "Voreinstellung", "Préréglage", "Preajuste", "预设", "Predefinição", "Пресет" };
        m["fc_open_editor"] = new[] { "Open editor…", "Otwórz edytor…", "Editor öffnen…", "Ouvrir l'éditeur…", "Abrir editor…", "打开编辑器…", "Abrir editor…", "Открыть редактор…" };
        m["fc_preset_auto"] = new[] { "Auto (stock)", "Auto (fabryczna)", "Auto (Standard)", "Auto (d'origine)", "Auto (de fábrica)", "自动（原厂）", "Auto (de fábrica)", "Авто (заводская)" };
        m["fc_ps_save"]     = new[] { "Save", "Zapisz", "Speichern", "Enregistrer", "Guardar", "保存", "Salvar", "Сохранить" };
        m["fc_ps_saveas"]   = new[] { "Save as…", "Zapisz jako…", "Speichern unter…", "Enregistrer sous…", "Guardar como…", "另存为…", "Salvar como…", "Сохранить как…" };
        m["fc_ps_rename"]   = new[] { "Rename…", "Zmień nazwę…", "Umbenennen…", "Renommer…", "Renombrar…", "重命名…", "Renomear…", "Переименовать…" };
        m["fc_ps_delete"]   = new[] { "Delete", "Usuń", "Löschen", "Supprimer", "Eliminar", "删除", "Excluir", "Удалить" };
        m["fc_ps_import"]   = new[] { "Import…", "Import…", "Importieren…", "Importer…", "Importar…", "导入…", "Importar…", "Импорт…" };
        m["fc_ps_export"]   = new[] { "Export…", "Eksport…", "Exportieren…", "Exporter…", "Exportar…", "导出…", "Exportar…", "Экспорт…" };
        m["fc_ps_share"]    = new[] { "Share…", "Udostępnij…", "Teilen…", "Partager…", "Compartir…", "分享…", "Compartilhar…", "Поделиться…" };
        m["fc_ps_name"]     = new[] { "Preset name", "Nazwa presetu", "Name der Voreinstellung", "Nom du préréglage", "Nombre del preajuste", "预设名称", "Nome da predefinição", "Название пресета" };
        m["fc_ps_exists"]   = new[] { "A preset with this name already exists.", "Preset o tej nazwie już istnieje.", "Eine Voreinstellung mit diesem Namen existiert bereits.", "Un préréglage de ce nom existe déjà.", "Ya existe un preajuste con ese nombre.", "已存在同名预设。", "Já existe uma predefinição com esse nome.", "Пресет с таким именем уже существует." };
        m["fc_ps_invalid"]  = new[] { "This is not a valid fan-curve preset file.", "To nie jest prawidłowy plik presetu krzywej.", "Dies ist keine gültige Lüfterkurven-Voreinstellungsdatei.", "Ce n'est pas un fichier de préréglage de courbe valide.", "No es un archivo de preajuste de curva válido.", "这不是有效的风扇曲线预设文件。", "Este não é um arquivo de predefinição de curva válido.", "Это не корректный файл пресета кривой." };
        m["fc_ps_del_confirm"] = new[] { "Delete preset \"{0}\"?", "Usunąć preset \"{0}\"?", "Voreinstellung \"{0}\" löschen?", "Supprimer le préréglage \"{0}\" ?", "¿Eliminar el preajuste \"{0}\"?", "删除预设\"{0}\"？", "Excluir a predefinição \"{0}\"?", "Удалить пресет \"{0}\"?" };
        m["fc_assign"]      = new[] { "Curve per profile (Silent stays stock):", "Krzywa per profil (Silent zawsze fabryczny):", "Kurve pro Profil (Silent bleibt Standard):", "Courbe par profil (Silent reste d'origine) :", "Curva por perfil (Silent siempre de fábrica):", "按配置文件应用曲线（Silent 始终原厂）：", "Curva por perfil (Silent sempre de fábrica):", "Кривая на профиль (Silent всегда заводской):" };
    }

    private static void L03(Dictionary<string, string[]> m)
    {
        m["log_curve_preset"] = new[] { "Fan curve preset applied: {0}", "Zastosowano preset krzywej: {0}", "Lüfterkurven-Voreinstellung angewendet: {0}", "Préréglage de courbe appliqué : {0}", "Preajuste de curva aplicado: {0}", "已应用风扇曲线预设：{0}", "Predefinição de curva aplicada: {0}", "Применён пресет кривой: {0}" };

        // ---- status: history sub-tab ----
        m["st_sub_history"] = new[] { "History", "Historia", "Verlauf", "Historique", "Historial", "历史", "Histórico", "История" };
        m["st_hist_temps"]  = new[] { "Temperatures (°C)", "Temperatury (°C)", "Temperaturen (°C)", "Températures (°C)", "Temperaturas (°C)", "温度（°C）", "Temperaturas (°C)", "Температуры (°C)" };
        m["st_hist_fans"]   = new[] { "Fans (duty %)", "Wentylatory (wypełnienie %)", "Lüfter (Duty %)", "Ventilateurs (charge %)", "Ventiladores (ciclo %)", "风扇（占空比 %）", "Ventoinhas (ciclo %)", "Вентиляторы (нагрузка %)" };
        m["st_hist_empty"]  = new[] { "Collecting data…", "Zbieranie danych…", "Daten werden gesammelt…", "Collecte des données…", "Recopilando datos…", "正在收集数据…", "Coletando dados…", "Сбор данных…" };
        m["st_hist_rpm"]    = new[] { "Fan speed (RPM)", "Obroty wentylatorów (RPM)", "Lüfterdrehzahl (RPM)", "Vitesse des ventilateurs (RPM)", "Velocidad de ventiladores (RPM)", "风扇转速（RPM）", "Velocidade das ventoinhas (RPM)", "Обороты вентиляторов (RPM)" };
        m["mdl_c_thanks"]   = new[] { "Thanks", "Podziękowania", "Dank", "Merci", "Gracias", "致谢", "Agradecimentos", "Благодарности" };
        m["st_hist_export"] = new[] { "Export…", "Eksport…", "Exportieren…", "Exporter…", "Exportar…", "导出…", "Exportar…", "Экспорт…" };

        // ---- FPS / gaming (overlay metrics, Status → Gaming, game-session report) ----
        m["ov_m_fps"]       = new[] { "FPS", "FPS", "FPS", "FPS", "FPS", "FPS", "FPS", "FPS" };
        m["ov_m_frametime"] = new[] { "Frametime", "Czas klatki", "Frametime", "Temps d'image", "Tiempo de fotograma", "帧生成时间", "Tempo de quadro", "Время кадра" };
        m["st_sub_gaming"]  = new[] { "Gaming", "Gry", "Gaming", "Jeu", "Juegos", "游戏", "Jogos", "Игры" };
        m["st_hist_fps"]    = new[] { "FPS", "FPS", "FPS", "FPS", "FPS", "FPS", "FPS", "FPS" };
        m["gm_game"]        = new[] { "Game: {0}", "Gra: {0}", "Spiel: {0}", "Jeu : {0}", "Juego: {0}", "游戏：{0}", "Jogo: {0}", "Игра: {0}" };
        m["gm_none"]        = new[]
        {
            "No game detected. Start a game while this tab or the overlay is open and FPS appears automatically.",
            "Nie wykryto gry. Uruchom grę przy otwartej tej zakładce lub włączonym overlay, a FPS pojawi się automatycznie.",
            "Kein Spiel erkannt. Starte ein Spiel, während dieser Tab oder das Overlay offen ist - die FPS erscheinen automatisch.",
            "Aucun jeu détecté. Lancez un jeu pendant que cet onglet ou l'overlay est ouvert - les FPS apparaissent automatiquement.",
            "No se detectó ningún juego. Inicia un juego con esta pestaña o el overlay abiertos y los FPS aparecerán automáticamente.",
            "未检测到游戏。在此标签页或悬浮窗打开时启动游戏，FPS 会自动显示。",
            "Nenhum jogo detectado. Inicie um jogo com esta aba ou o overlay abertos e o FPS aparecerá automaticamente.",
            "Игра не обнаружена. Запустите игру, пока открыта эта вкладка или оверлей - FPS появится автоматически."
        };
        m["gm_chart"]       = new[] { "Frametime — last 60 s", "Czas klatki — ostatnie 60 s", "Frametime — letzte 60 s", "Temps d'image — 60 dernières s", "Tiempo de fotograma — últimos 60 s", "帧生成时间 — 最近 60 秒", "Tempo de quadro — últimos 60 s", "Время кадра — последние 60 с" };
        m["gm_chart_empty"] = new[] { "Waiting for frames…", "Czekam na klatki…", "Warte auf Frames…", "En attente d'images…", "Esperando fotogramas…", "等待帧数据…", "Aguardando quadros…", "Ожидание кадров…" };
        m["gm_stut"]        = new[] { "Stutters", "Przycięcia", "Ruckler", "Saccades", "Tirones", "卡顿", "Engasgos", "Статтеры" };
        m["gm_last"]        = new[] { "Last game session", "Ostatnia sesja gry", "Letzte Spielsitzung", "Dernière session de jeu", "Última sesión de juego", "上次游戏会话", "Última sessão de jogo", "Последняя игровая сессия" };
        m["gm_last_none"]   = new[]
        {
            "Play for at least a minute — a summary lands here when the game exits.",
            "Zagraj co najmniej minutę — podsumowanie pojawi się tu po zamknięciu gry.",
            "Spiele mindestens eine Minute — die Zusammenfassung erscheint hier nach dem Beenden des Spiels.",
            "Jouez au moins une minute — le résumé apparaît ici à la fermeture du jeu.",
            "Juega al menos un minuto — el resumen aparecerá aquí al cerrar el juego.",
            "至少游玩一分钟，游戏退出后摘要会显示在这里。",
            "Jogue por pelo menos um minuto — o resumo aparece aqui quando o jogo fechar.",
            "Поиграйте хотя бы минуту — сводка появится здесь после закрытия игры."
        };
        m["gm_game_lbl"]    = new[] { "Game", "Gra", "Spiel", "Jeu", "Juego", "游戏", "Jogo", "Игра" };
        m["gm_dur"]         = new[] { "Duration", "Czas trwania", "Dauer", "Durée", "Duración", "时长", "Duração", "Длительность" };
        m["gm_fps_row"]     = new[] { "avg {0} · min {1} · max {2}", "śr. {0} · min {1} · maks {2}", "Ø {0} · min {1} · max {2}", "moy. {0} · min {1} · max {2}", "med. {0} · mín {1} · máx {2}", "平均 {0} · 最低 {1} · 最高 {2}", "méd. {0} · mín {1} · máx {2}", "ср. {0} · мин {1} · макс {2}" };
        m["gm_frames"]      = new[] { "Frames", "Klatki", "Frames", "Images", "Fotogramas", "帧数", "Quadros", "Кадры" };
        m["gm_temp_max"]    = new[] { "Max temperature", "Maks. temperatura", "Max. Temperatur", "Température max", "Temperatura máx.", "最高温度", "Temperatura máx.", "Макс. температура" };
        m["gm_rpm_avg"]     = new[] { "Avg fan RPM", "Śr. obroty (RPM)", "Ø Lüfter-RPM", "RPM moyen ventilos", "RPM medio ventilador", "平均风扇转速", "RPM médio ventoinha", "Ср. обороты (RPM)" };
        m["gm_profile"]     = new[] { "Profile", "Profil", "Profil", "Profil", "Perfil", "配置文件", "Perfil", "Профиль" };
        m["gm_sess_title"]  = new[] { "Game session", "Sesja gry", "Spielsitzung", "Session de jeu", "Sesión de juego", "游戏会话", "Sessão de jogo", "Игровая сессия" };
        m["gm_sess_text"]   = new[] { "{0} · avg {1} FPS · 1% low {2}", "{0} · śr. {1} FPS · 1% low {2}", "{0} · Ø {1} FPS · 1% low {2}", "{0} · moy. {1} FPS · 1% low {2}", "{0} · med. {1} FPS · 1% low {2}", "{0} · 平均 {1} FPS · 1% low {2}", "{0} · méd. {1} FPS · 1% low {2}", "{0} · ср. {1} FPS · 1% low {2}" };
        m["gm_sess_ec"]     = new[] { " · CPU max {0}°C", " · CPU maks. {0}°C", " · CPU max. {0}°C", " · CPU max {0}°C", " · CPU máx. {0}°C", " · CPU 最高 {0}°C", " · CPU máx. {0}°C", " · CPU макс. {0}°C" };
        m["log_src_game"]   = new[] { "game", "gra", "Spiel", "jeu", "juego", "游戏", "jogo", "игра" };
        m["log_src_restore"] = new[] { "restore", "przywracanie", "Wiederherstellung", "restauration", "restauración", "恢复", "restauração", "восстановление" };
        m["set_sess_popup"] = new[] { "Game-session popup", "Okienko podsumowania sesji", "Sitzungs-Popup", "Fenêtre de résumé de session", "Ventana de resumen de sesión", "游戏会话弹窗", "Popup de resumo da sessão", "Окно сводки сессии" };
        m["set_sess_desc"]  = new[]
        {
            "When a game exits, GhostDeck shows a summary popup: FPS, 1% low, temperatures and fan RPM, with one-click PNG save and JSON/CSV export. Recent sessions are kept in Status → Gaming, where you can browse and export them later.",
            "Po zamknięciu gry GhostDeck pokazuje okienko z podsumowaniem: FPS, 1% low, temperatury i obroty wentylatorów, z zapisem PNG i eksportem JSON/CSV jednym kliknięciem. Ostatnie sesje znajdziesz w Status → Gry, gdzie można je przeglądać i eksportować.",
            "Beim Beenden eines Spiels zeigt GhostDeck ein Zusammenfassungs-Popup: FPS, 1% low, Temperaturen und Lüfterdrehzahl, mit PNG-Speicherung und JSON/CSV-Export per Klick. Die letzten Sitzungen bleiben unter Status → Gaming zum Ansehen und Exportieren.",
            "À la fermeture d'un jeu, GhostDeck affiche un résumé : FPS, 1% low, températures et RPM des ventilateurs, avec enregistrement PNG et export JSON/CSV en un clic. Les sessions récentes restent dans Statut → Jeu pour les consulter et les exporter.",
            "Al cerrar un juego, GhostDeck muestra una ventana de resumen: FPS, 1% low, temperaturas y RPM de los ventiladores, con guardado PNG y exportación JSON/CSV con un clic. Las sesiones recientes quedan en Estado → Juegos para verlas y exportarlas.",
            "游戏退出时，GhostDeck 会显示会话摘要弹窗：FPS、1% low、温度和风扇转速，可一键保存 PNG 或导出 JSON/CSV。最近的会话保存在 状态 → 游戏 中，可随时查看和导出。",
            "Ao fechar um jogo, o GhostDeck mostra um popup de resumo: FPS, 1% low, temperaturas e RPM das ventoinhas, com salvamento em PNG e exportação JSON/CSV em um clique. As sessões recentes ficam em Status → Jogos para consulta e exportação.",
            "После закрытия игры GhostDeck показывает окно сводки: FPS, 1% low, температуры и обороты вентиляторов, с сохранением PNG и экспортом JSON/CSV в один клик. Последние сессии хранятся в Статус → Игры, где их можно просматривать и экспортировать."
        };
        m["set_sess_secs"]  = new[] { "Popup visible for", "Czas widoczności okienka", "Popup sichtbar für", "Durée d'affichage", "Duración del popup", "弹窗显示时长", "Duração do popup", "Время показа окна" };
        m["sess_always"]    = new[] { "Until closed", "Aż do zamknięcia", "Bis zum Schließen", "Jusqu'à fermeture", "Hasta cerrarla", "直到关闭", "Até fechar", "До закрытия" };
        m["set_sess_keep"]  = new[] { "Remembered game sessions", "Zapamiętane sesje gier", "Gespeicherte Spielsitzungen", "Sessions de jeu mémorisées", "Sesiones de juego guardadas", "保存的游戏会话数", "Sessões de jogo guardadas", "Сохранённые игровые сессии" };
        m["set_restore_profile"] = new[]
        {
            "Restore profile after wake / at startup",
            "Przywracaj profil po wybudzeniu i przy starcie",
            "Profil nach Aufwachen / beim Start wiederherstellen",
            "Restaurer le profil après réveil / au démarrage",
            "Restaurar perfil al despertar / al iniciar",
            "唤醒 / 启动时恢复配置文件",
            "Restaurar perfil ao acordar / ao iniciar",
            "Восстанавливать профиль после пробуждения / при запуске"
        };
        m["set_restore_curve"] = new[]
        {
            "Restore fan curve after wake / at startup",
            "Przywracaj krzywą wentylatorów po wybudzeniu i przy starcie",
            "Lüfterkurve nach Aufwachen / beim Start wiederherstellen",
            "Restaurer la courbe de ventilation après réveil / au démarrage",
            "Restaurar curva de ventilador al despertar / al iniciar",
            "唤醒 / 启动时恢复风扇曲线",
            "Restaurar curva de ventoinha ao acordar / ao iniciar",
            "Восстанавливать кривую вентиляторов после пробуждения / при запуске"
        };
        m["log_curve_restore"] = new[] { "Fan curve restored: {0}", "Przywrócono krzywą: {0}", "Lüfterkurve wiederhergestellt: {0}", "Courbe restaurée : {0}", "Curva restaurada: {0}", "已恢复风扇曲线：{0}", "Curva restaurada: {0}", "Кривая восстановлена: {0}" };
        m["fc_custom"]      = new[] { "custom curve", "własna krzywa", "eigene Kurve", "courbe personnalisée", "curva personalizada", "自定义曲线", "curva personalizada", "своя кривая" };
        m["set_grp_diag"]   = new[] { "Diagnostics", "Diagnostyka", "Diagnose", "Diagnostic", "Diagnóstico", "诊断", "Diagnóstico", "Диагностика" };
        m["diag_save"]      = new[] { "Save diagnostic package…", "Zapisz pakiet diagnostyczny…", "Diagnosepaket speichern…", "Enregistrer le paquet de diagnostic…", "Guardar paquete de diagnóstico…", "保存诊断包…", "Salvar pacote de diagnóstico…", "Сохранить пакет диагностики…" };
        m["set_grp_batt"]   = new[] { "Battery health", "Zdrowie baterii", "Akkuzustand", "Santé de la batterie", "Salud de la batería", "电池健康", "Saúde da bateria", "Состояние батареи" };
        m["bh_design"]      = new[] { "Design capacity", "Pojemność projektowa", "Nennkapazität", "Capacité nominale", "Capacidad de diseño", "设计容量", "Capacidade de projeto", "Проектная ёмкость" };
        m["bh_full"]        = new[] { "Full-charge capacity", "Pojemność po pełnym ładowaniu", "Aktuelle Vollladekapazität", "Capacité à pleine charge", "Capacidad a plena carga", "满充容量", "Capacidade em carga plena", "Ёмкость при полном заряде" };
    }

    private static void L04(Dictionary<string, string[]> m)
    {
        m["bh_wear"]        = new[] { "Wear", "Zużycie", "Verschleiß", "Usure", "Desgaste", "损耗", "Desgaste", "Износ" };
        m["bh_cycles"]      = new[] { "Charge cycles", "Cykle ładowania", "Ladezyklen", "Cycles de charge", "Ciclos de carga", "充电循环", "Ciclos de carga", "Циклы зарядки" };
        m["ov_m_ssd"]       = new[] { "SSD temp", "Temp. SSD", "SSD-Temp.", "Temp. SSD", "Temp. SSD", "SSD 温度", "Temp. SSD", "Темп. SSD" };
        m["ov_m_batttime"]  = new[] { "Battery time left", "Pozostały czas baterii", "Restlaufzeit", "Autonomie restante", "Tiempo de batería restante", "剩余电池时间", "Tempo restante de bateria", "Оставшееся время батареи" };
        m["st_left"]        = new[] { "Left", "Pozostało", "Verbleibt", "Restant", "Restante", "剩余", "Restante", "Осталось" };
        m["tier_telemetry"] = new[] { "temperatures only", "tylko temperatury", "nur Temperaturen", "températures seules", "solo temperaturas", "仅温度", "somente temperaturas", "только температуры" };
        m["set_fb_timer"]   = new[] { "Turn Fan Boost off automatically after", "Wyłączaj Fan Boost automatycznie po", "Fan Boost automatisch ausschalten nach", "Désactiver Fan Boost automatiquement après", "Desactivar Fan Boost automáticamente tras", "自动关闭 Fan Boost 的时间", "Desligar o Fan Boost automaticamente após", "Автовыключение Fan Boost через" };
        m["fb_never"]       = new[] { "Never", "Nigdy", "Nie", "Jamais", "Nunca", "从不", "Nunca", "Никогда" };
        m["fb_custom"]      = new[] { "Custom…", "Własna…", "Eigene…", "Personnalisé…", "Personalizado…", "自定义…", "Personalizado…", "Своё…" };
        m["fb_custom_ask"]  = new[] { "Fan Boost auto-off after how many minutes? (1-120)", "Po ilu minutach wyłączyć Fan Boost? (1-120)", "Fan Boost nach wie vielen Minuten ausschalten? (1-120)", "Désactiver Fan Boost après combien de minutes ? (1-120)", "¿Tras cuántos minutos desactivar Fan Boost? (1-120)", "多少分钟后自动关闭 Fan Boost？（1-120）", "Desligar o Fan Boost após quantos minutos? (1-120)", "Через сколько минут выключить Fan Boost? (1-120)" };
        m["fb_auto_off"]    = new[] { "Timer elapsed - back to the profile's fans", "Minął czas - wentylatory wracają do profilu", "Zeit abgelaufen - zurück zu den Profil-Lüftern", "Minuteur écoulé - retour aux ventilateurs du profil", "Tiempo cumplido - vuelta a los ventiladores del perfil", "计时结束 - 风扇回到配置文件设置", "Tempo esgotado - ventoinhas voltam ao perfil", "Время истекло - вентиляторы возвращаются к профилю" };
        m["fb_secs"]        = new[] { "{0} s", "{0} s", "{0} s", "{0} s", "{0} s", "{0} 秒", "{0} s", "{0} с" };
        m["fb_mins"]        = new[] { "{0} min", "{0} min", "{0} Min.", "{0} min", "{0} min", "{0} 分钟", "{0} min", "{0} мин" };
        m["telemetry_note"] = new[] {
            "This laptop's firmware does not provide MSI's EC control interface, so profiles, fan curves and the charge limit are unavailable. GhostDeck reads CPU/GPU temperature from MSI's WMI sensor blocks instead.",
            "Firmware tego laptopa nie udostępnia interfejsu sterowania EC firmy MSI, więc profile, krzywe wentylatorów i limit ładowania są niedostępne. GhostDeck odczytuje temperatury CPU/GPU z bloków czujników WMI firmy MSI.",
            "Die Firmware dieses Laptops bietet keine EC-Steuerschnittstelle von MSI, daher sind Profile, Lüfterkurven und Ladelimit nicht verfügbar. GhostDeck liest stattdessen die CPU-/GPU-Temperatur aus MSIs WMI-Sensorblöcken.",
            "Le firmware de cet ordinateur ne fournit pas l'interface de contrôle EC de MSI : profils, courbes de ventilation et limite de charge sont indisponibles. GhostDeck lit la température CPU/GPU via les blocs capteurs WMI de MSI.",
            "El firmware de este portátil no ofrece la interfaz de control EC de MSI, así que perfiles, curvas de ventilador y límite de carga no están disponibles. GhostDeck lee la temperatura de CPU/GPU desde los bloques de sensores WMI de MSI.",
            "本机固件未提供 MSI 的 EC 控制接口，因此无法使用配置文件、风扇曲线和充电限制。GhostDeck 改为从 MSI 的 WMI 传感器块读取 CPU/GPU 温度。",
            "O firmware deste notebook não fornece a interface de controle do EC da MSI, portanto perfis, curvas de ventoinha e limite de carga não estão disponíveis. O GhostDeck lê a temperatura de CPU/GPU dos blocos de sensores WMI da MSI.",
            "Прошивка этого ноутбука не предоставляет интерфейс управления EC от MSI, поэтому профили, кривые вентиляторов и лимит заряда недоступны. GhostDeck считывает температуру CPU/GPU из блоков датчиков WMI MSI." };
        m["diag_desc"] = new[] {
            "The zip contains: a read-only EC dump (or the exact error it produced), MSI's WMI sensor blocks, your settings, the change history, the error log and version info. No personal data.",
            "Zip zawiera: zrzut EC (tylko odczyt, lub dokładny błąd odczytu), bloki czujników WMI firmy MSI, ustawienia, historię zmian, dziennik błędów i informacje o wersji. Bez danych osobistych.",
            "Das Zip enthält: einen EC-Dump (nur Lesezugriff, oder den genauen Fehler), MSIs WMI-Sensorblöcke, Ihre Einstellungen, den Änderungsverlauf, das Fehlerprotokoll und Versionsinfos. Keine persönlichen Daten.",
            "Le zip contient : un dump EC (lecture seule, ou l'erreur exacte), les blocs capteurs WMI de MSI, vos réglages, l'historique des changements, le journal d'erreurs et les versions. Aucune donnée personnelle.",
            "El zip contiene: un volcado del EC (solo lectura, o el error exacto), los bloques de sensores WMI de MSI, tus ajustes, el historial de cambios, el registro de errores y las versiones. Sin datos personales.",
            "压缩包包含：EC 转储（只读，或确切的读取错误）、MSI 的 WMI 传感器块、您的设置、更改历史、错误日志和版本信息。不含个人数据。",
            "O zip contém: um dump do EC (somente leitura, ou o erro exato), os blocos de sensores WMI da MSI, suas configurações, o histórico de mudanças, o log de erros e informações de versão. Sem dados pessoais.",
            "Архив содержит: дамп EC (только чтение, либо точную ошибку), блоки датчиков WMI MSI, ваши настройки, историю изменений, журнал ошибок и сведения о версиях. Без личных данных." };
        m["st_hist_fps_hint"] = new[]
        {
            "Fills in while a game is running (open the Gaming tab or the overlay to start the FPS monitor).",
            "Wypełnia się, gdy działa gra (otwórz zakładkę Gry albo overlay, by uruchomić monitor FPS).",
            "Füllt sich, während ein Spiel läuft (öffne den Gaming-Tab oder das Overlay, um den FPS-Monitor zu starten).",
            "Se remplit pendant qu'un jeu tourne (ouvrez l'onglet Jeu ou l'overlay pour démarrer le moniteur FPS).",
            "Se llena mientras un juego está en marcha (abre la pestaña Juegos o el overlay para iniciar el monitor de FPS).",
            "游戏运行时填充（打开游戏标签页或悬浮窗以启动 FPS 监视器）。",
            "Preenche-se enquanto um jogo está em execução (abra a aba Jogos ou o overlay para iniciar o monitor de FPS).",
            "Заполняется, пока запущена игра (откройте вкладку Игры или оверлей, чтобы запустить монитор FPS)."
        };

        // ---- display refresh-rate auto-switch (discussion #18) ----
        m["set_refresh_toggle"] = new[] { "Switch refresh rate on AC / battery", "Przełączaj odświeżanie przy AC / baterii", "Bildwiederholrate bei Netz / Akku umschalten", "Changer la fréquence sur secteur / batterie", "Cambiar la frecuencia con CA / batería", "接通/电池时切换刷新率", "Alternar taxa de atualização na CA / bateria", "Переключать частоту при сети / батарее" };
        m["set_refresh_ac"]     = new[] { "Refresh on AC", "Odświeżanie na zasilaczu", "Frequenz am Netz", "Fréquence sur secteur", "Frecuencia con CA", "接通电源时刷新率", "Taxa na CA", "Частота от сети" };
        m["set_refresh_batt"]   = new[] { "Refresh on battery", "Odświeżanie na baterii", "Frequenz im Akkubetrieb", "Fréquence sur batterie", "Frecuencia con batería", "电池模式刷新率", "Taxa na bateria", "Частота от батареи" };
        m["ref_keep"]           = new[] { "No change", "Bez zmiany", "Keine Änderung", "Aucun changement", "Sin cambio", "不更改", "Sem alteração", "Без изменения" };
        m["ref_title"]          = new[] { "Refresh rate", "Odświeżanie", "Bildwiederholrate", "Fréquence d'affichage", "Frecuencia de refresco", "刷新率", "Taxa de atualização", "Частота обновления" };
        m["log_src_display"]    = new[] { "display", "ekran", "Display", "écran", "pantalla", "显示器", "tela", "экран" };
        m["st_hist_now"]    = new[] { "now", "teraz", "jetzt", "maintenant", "ahora", "现在", "agora", "сейчас" };
        m["ta_alert_text"]   = new[]
        {
            "CPU {0}°C / GPU {1}°C: above {2}°C for {3} s",
            "CPU {0}°C / GPU {1}°C: powyżej {2}°C przez {3} s",
            "CPU {0}°C / GPU {1}°C: über {2}°C seit {3} s",
            "CPU {0}°C / GPU {1}°C : au-dessus de {2}°C depuis {3} s",
            "CPU {0}°C / GPU {1}°C: por encima de {2}°C durante {3} s",
            "CPU {0}°C / GPU {1}°C：超过 {2}°C 已持续 {3} 秒",
            "CPU {0}°C / GPU {1}°C: acima de {2}°C por {3} s",
            "CPU {0}°C / GPU {1}°C: выше {2}°C в течение {3} с"
        };

        // ---- panic reset hotkey ----
        m["hk_panic"]        = new[] { "Panic reset", "Reset awaryjny", "Not-Reset", "Réinitialisation d'urgence", "Reinicio de emergencia", "紧急重置", "Reset de emergência", "Аварийный сброс" };
        m["panic_sub"]       = new[]
        {
            "Balanced profile, Fan Boost off, fans auto",
            "Profil Balanced, Fan Boost wył., wentylatory auto",
            "Profil Balanced, Fan Boost aus, Lüfter auto",
            "Profil Balanced, Fan Boost désactivé, ventilateurs auto",
            "Perfil Balanced, Fan Boost desactivado, ventiladores en auto",
            "Balanced 配置文件，Fan Boost 关闭，风扇自动",
            "Perfil Balanced, Fan Boost desligado, ventoinhas em auto",
            "Профиль Balanced, Fan Boost выкл., вентиляторы авто"
        };

        // ---- settings backup (export / import) ----
        m["set_grp_backup"]  = new[] { "Backup", "Kopia zapasowa", "Sicherung", "Sauvegarde", "Copia de seguridad", "备份", "Backup", "Резервная копия" };
        m["set_export"]      = new[] { "Export settings…", "Eksportuj ustawienia…", "Einstellungen exportieren…", "Exporter les réglages…", "Exportar ajustes…", "导出设置…", "Exportar configurações…", "Экспорт настроек…" };
        m["set_import"]      = new[] { "Import settings…", "Importuj ustawienia…", "Einstellungen importieren…", "Importer les réglages…", "Importar ajustes…", "导入设置…", "Importar configurações…", "Импорт настроек…" };
        m["imp_ok"]          = new[] { "Settings imported.", "Ustawienia zaimportowane.", "Einstellungen importiert.", "Réglages importés.", "Ajustes importados.", "设置已导入。", "Configurações importadas.", "Настройки импортированы." };
        m["imp_err"]         = new[] { "This is not a valid GhostDeck settings file.", "To nie jest prawidłowy plik ustawień GhostDeck.", "Dies ist keine gültige GhostDeck-Einstellungsdatei.", "Ce n'est pas un fichier de réglages GhostDeck valide.", "No es un archivo de ajustes de GhostDeck válido.", "这不是有效的 GhostDeck 设置文件。", "Este não é um arquivo de configurações válido do GhostDeck.", "Это не корректный файл настроек GhostDeck." };
        m["bk_err"]          = new[] { "Operation failed: {0}", "Operacja nie powiodła się: {0}", "Vorgang fehlgeschlagen: {0}", "Échec de l'opération : {0}", "La operación falló: {0}", "操作失败：{0}", "Falha na operação: {0}", "Операция не удалась: {0}" };

        // ---- firmware-change warning ----
        m["menu_fw_ack"]      = new[] { "⚠ Firmware changed — verify model", "⚠ Zmiana firmware — zweryfikuj model", "⚠ Firmware geändert — Modell prüfen", "⚠ Firmware modifié — vérifier le modèle", "⚠ Firmware cambiado — verificar modelo", "⚠ 固件已更改 — 请核对型号", "⚠ Firmware alterado — verificar modelo", "⚠ Прошивка изменена — проверьте модель" };
        m["fw_changed_title"] = new[] { "EC firmware changed", "Zmieniono firmware EC", "EC-Firmware geändert", "Firmware EC modifié", "Firmware EC cambiado", "EC 固件已更改", "Firmware EC alterado", "Прошивка EC изменена" };
        m["fw_changed_text"]  = new[] { "EC firmware changed — automatic writes are paused. Verify the model again, then click to acknowledge.", "Firmware EC uległ zmianie — automatyczne zapisy wstrzymane. Zweryfikuj model ponownie, potem kliknij, aby potwierdzić.", "EC-Firmware hat sich geändert — automatische Schreibvorgänge pausiert. Modell erneut prüfen und zum Bestätigen klicken.", "Le firmware EC a changé — écritures automatiques suspendues. Vérifiez le modèle puis cliquez pour confirmer.", "El firmware EC cambió — escrituras automáticas en pausa. Verifica el modelo y haz clic para confirmar.", "EC 固件已更改 — 自动写入已暂停。请重新核对型号后点击确认。", "O firmware EC mudou — gravações automáticas pausadas. Verifique o modelo e clique para confirmar.", "Прошивка EC изменилась — автозапись приостановлена. Проверьте модель и нажмите для подтверждения." };
        m["log_fw_changed"]   = new[] { "EC firmware changed: {0} → {1} (auto-writes blocked)", "Zmiana firmware EC: {0} → {1} (auto-zapisy zablokowane)", "EC-Firmware geändert: {0} → {1} (Auto-Schreiben blockiert)", "Firmware EC modifié : {0} → {1} (écritures auto bloquées)", "Firmware EC cambiado: {0} → {1} (escrituras auto bloqueadas)", "EC 固件已更改：{0} → {1}（自动写入已阻止）", "Firmware EC alterado: {0} → {1} (gravações auto bloqueadas)", "Прошивка EC изменена: {0} → {1} (автозапись заблокирована)" };
        m["log_fw_ack"]       = new[] { "Firmware change acknowledged", "Potwierdzono zmianę firmware", "Firmware-Änderung bestätigt", "Changement de firmware confirmé", "Cambio de firmware confirmado", "已确认固件更改", "Alteração de firmware confirmada", "Изменение прошивки подтверждено" };
        m["tab_scenarios"]  = new[] { "Scenarios", "Scenariusze", "Szenarien", "Scénarios", "Escenarios", "场景", "Cenários", "Сценарии" };
        m["tab_updates"]    = new[] { "Updates", "Aktualizacje", "Updates", "Mises à jour", "Actualizaciones", "更新", "Atualizações", "Обновления" };
        m["scen_title"]     = new[] { "Choose a scenario", "Wybierz scenariusz", "Szenario wählen", "Choisir un scénario", "Elige un escenario", "选择场景", "Escolha um cenário", "Выберите сценарий" };
        m["scen_autoswitch"]= new[] { "Auto-switch on AC / battery", "Auto-przełączanie AC / bateria", "Auto-Wechsel bei Netz / Akku", "Bascule auto secteur / batterie", "Cambio automático CA / batería", "接通/电池自动切换", "Troca automática CA / bateria", "Авто-переключение сеть / батарея" };
        m["gen_off"]        = new[] { "Off", "Wyłączone", "Aus", "Désactivé", "Apagado", "关闭", "Desligado", "Выкл" };
        m["gen_off_short"]  = new[] { "Off", "Wył.", "Aus", "Off", "No", "关", "Não", "Выкл" };
        m["upd_installed"]  = new[] { "Installed version", "Zainstalowana wersja", "Installierte Version", "Version installée", "Versión instalada", "已安装版本", "Versão instalada", "Установленная версия" };
        m["upd_latest_ok"]  = new[] { "You're on the latest version", "Używasz najnowszej wersji", "Sie nutzen die neueste Version", "Vous avez la dernière version", "Tienes la última versión", "已是最新版本", "Você está na versão mais recente", "У вас последняя версия" };
    }

    private static void L05(Dictionary<string, string[]> m)
    {
        m["upd_check_now"]  = new[] { "Check now", "Sprawdź teraz", "Jetzt prüfen", "Vérifier maintenant", "Comprobar ahora", "立即检查", "Verificar agora", "Проверить сейчас" };
        m["upd_checking"]   = new[] { "Checking…", "Sprawdzanie…", "Wird geprüft…", "Vérification…", "Comprobando…", "正在检查…", "Verificando…", "Проверка…" };
        m["upd_last_checked"] = new[] { "Last checked: {0}", "Ostatnio sprawdzano: {0}", "Zuletzt geprüft: {0}", "Dernière vérification : {0}", "Última comprobación: {0}", "上次检查：{0}", "Última verificação: {0}", "Последняя проверка: {0}" };
        m["upd_never"]      = new[] { "never", "nigdy", "nie", "jamais", "nunca", "从未", "nunca", "никогда" };
        m["upd_available"]  = new[] { "New version {0} is available", "Dostępna nowa wersja {0}", "Neue Version {0} verfügbar", "Nouvelle version {0} disponible", "Nueva versión {0} disponible", "有新版本 {0}", "Nova versão {0} disponível", "Доступна новая версия {0}" };
        m["upd_install"]    = new[] { "Install {0}", "Zainstaluj {0}", "{0} installieren", "Installer {0}", "Instalar {0}", "安装 {0}", "Instalar {0}", "Установить {0}" };
        m["upd_downloading"] = new[] { "Downloading… {0}%", "Pobieranie… {0}%", "Wird heruntergeladen… {0}%", "Téléchargement… {0}%", "Descargando… {0}%", "正在下载… {0}%", "Baixando… {0}%", "Загрузка… {0}%" };
        m["upd_restarting"] = new[] { "Restarting to finish the update…", "Ponowne uruchamianie, aby dokończyć aktualizację…", "Neustart zum Abschluss des Updates…", "Redémarrage pour terminer la mise à jour…", "Reiniciando para finalizar la actualización…", "正在重启以完成更新…", "Reiniciando para concluir a atualização…", "Перезапуск для завершения обновления…" };
        m["upd_dl_failed"]  = new[] { "Download failed — opening the releases page.", "Pobieranie nie powiodło się - otwieram stronę wydań.", "Download fehlgeschlagen — Release-Seite wird geöffnet.", "Échec du téléchargement — ouverture de la page des versions.", "Error de descarga — abriendo la página de versiones.", "下载失败 — 正在打开发布页面。", "Falha no download — abrindo a página de versões.", "Сбой загрузки — открываю страницу релизов." };
        m["upd_download"]   = new[] { "Download", "Pobierz", "Herunterladen", "Télécharger", "Descargar", "下载", "Baixar", "Скачать" };
        m["upd_history"]    = new[] { "Release history", "Historia wydań", "Versionsverlauf", "Historique des versions", "Historial de versiones", "发布历史", "Histórico de versões", "История версий" };
        m["upd_details"]    = new[] { "Details", "Szczegóły", "Details", "Détails", "Detalles", "详情", "Detalhes", "Подробности" };
        m["upd_offline"]    = new[] { "Couldn't reach GitHub. Check your connection and try again.", "Nie udało się połączyć z GitHub. Sprawdź połączenie i spróbuj ponownie.", "GitHub nicht erreichbar. Verbindung prüfen und erneut versuchen.", "Impossible de joindre GitHub. Vérifiez la connexion et réessayez.", "No se pudo conectar con GitHub. Revisa la conexión e inténtalo de nuevo.", "无法连接 GitHub。请检查网络后重试。", "Não foi possível acessar o GitHub. Verifique a conexão e tente novamente.", "Не удалось подключиться к GitHub. Проверьте соединение и повторите." };
        m["upd_retry"]      = new[] { "Try again", "Spróbuj ponownie", "Erneut versuchen", "Réessayer", "Reintentar", "重试", "Tentar novamente", "Повторить" };
        m["upd_downloads"]  = new[] { "Downloads: {0}", "Pobrania: {0}", "Downloads: {0}", "Téléchargements : {0}", "Descargas: {0}", "下载次数：{0}", "Downloads: {0}", "Загрузки: {0}" };
        m["set_advanced"]   = new[] { "Advanced settings (colours, hotkeys)…", "Ustawienia zaawansowane (kolory, skróty)…", "Erweiterte Einstellungen (Farben, Tastenkürzel)…", "Paramètres avancés (couleurs, raccourcis)…", "Ajustes avanzados (colores, atajos)…", "高级设置（颜色、快捷键）…", "Configurações avançadas (cores, atalhos)…", "Расширенные настройки (цвета, горячие клавиши)…" };
        m["set_theme"]      = new[] { "Theme", "Motyw", "Design", "Thème", "Tema", "主题", "Tema", "Тема" };
        m["set_theme_light"]= new[] { "Light", "Jasny", "Hell", "Clair", "Claro", "浅色", "Claro", "Светлая" };
        m["set_theme_dark"] = new[] { "Dark", "Ciemny", "Dunkel", "Sombre", "Oscuro", "深色", "Escuro", "Тёмная" };
        m["menu_language"]  = new[] { "Language", "Język", "Sprache", "Langue", "Idioma", "语言", "Idioma", "Язык" };
        m["menu_exit"]      = new[] { "Exit", "Zamknij", "Beenden", "Quitter", "Salir", "退出", "Sair", "Выход" };

        m["set_hotkeys"]    = new[] { "Keyboard shortcuts", "Skróty klawiszowe", "Tastenkürzel", "Raccourcis clavier", "Atajos de teclado", "键盘快捷键", "Atalhos de teclado", "Горячие клавиши" };
        m["hk_all"]         = new[] { "All shortcuts", "Wszystkie skróty", "Alle Kürzel", "Tous les raccourcis", "Todos los atajos", "所有快捷键", "Todos os atalhos", "Все клавиши" };
        m["hk_none"]        = new[] { "(none)", "(brak)", "(keine)", "(aucun)", "(ninguno)", "（无）", "(nenhum)", "(нет)" };
        m["set_hint"]       = new[] { "Click a field and press a combo.  Esc / Delete = clear.", "Kliknij pole i wciśnij kombinację.  Esc / Delete = wyczyść.", "Feld anklicken und Kombination drücken.  Esc / Entf = löschen.", "Cliquez sur un champ et appuyez sur une combinaison.  Échap / Suppr = effacer.", "Haz clic en un campo y pulsa una combinación.  Esc / Supr = borrar.", "点击字段并按下组合键。Esc / Delete = 清除。", "Clique num campo e pressione uma combinação.  Esc / Delete = limpar.", "Нажмите поле и введите комбинацию.  Esc / Delete = очистить." };
        m["cycle"]          = new[] { "Cycle (next)", "Cykl (następny)", "Wechseln (nächstes)", "Cycle (suivant)", "Ciclo (siguiente)", "循环（下一个）", "Ciclo (próximo)", "Цикл (следующий)" };
        m["set_autostart"]  = new[] { "Start with Windows", "Uruchamiaj z Windowsem", "Mit Windows starten", "Démarrer avec Windows", "Iniciar con Windows", "随 Windows 启动", "Iniciar com o Windows", "Запускать с Windows" };
        m["set_default"]    = new[] { "Defaults", "Domyślne", "Standard", "Défaut", "Predeterminado", "默认", "Padrão", "По умолчанию" };
        m["set_save"]       = new[] { "Save", "Zapisz", "Speichern", "Enregistrer", "Guardar", "保存", "Salvar", "Сохранить" };
        m["set_close"]      = new[] { "Close", "Zamknij", "Schließen", "Fermer", "Cerrar", "关闭", "Fechar", "Закрыть" };
        m["set_saved"]      = new[] { "✓ Saved", "✓ Zapisano", "✓ Gespeichert", "✓ Enregistré", "✓ Guardado", "✓ 已保存", "✓ Salvo", "✓ Сохранено" };
        m["set_reset_hint"] = new[] { "Defaults restored (click Save).", "Przywrócono domyślne (kliknij Zapisz).", "Standard wiederhergestellt (Speichern).", "Valeurs par défaut (cliquez Enregistrer).", "Restaurado (haz clic en Guardar).", "已恢复默认（点击保存）。", "Padrões restaurados (clique em Salvar).", "Восстановлено (нажмите Сохранить)." };
        m["set_language"]   = new[] { "Language", "Język", "Sprache", "Langue", "Idioma", "语言", "Idioma", "Язык" };
        m["set_colors"]     = new[] { "Profile colors", "Kolory profili", "Profilfarben", "Couleurs des profils", "Colores de perfil", "配置文件颜色", "Cores dos perfis", "Цвета профилей" };
        m["set_colors_reset"] = new[] { "Restore default colors", "Przywróć domyślne kolory", "Standardfarben wiederherstellen", "Restaurer les couleurs par défaut", "Restaurar colores predeterminados", "恢复默认颜色", "Restaurar cores padrão", "Восстановить цвета по умолчанию" };
        m["set_app_icon"]   = new[] { "Application icon", "Ikona aplikacji", "App-Symbol", "Icône de l'application", "Icono de la aplicación", "应用图标", "Ícone do aplicativo", "Значок приложения" };
        m["icon_logo"]      = new[] { "GhostDeck logo", "Logotyp GhostDeck", "GhostDeck-Logo", "Logo GhostDeck", "Logotipo GhostDeck", "GhostDeck 标志", "Logotipo GhostDeck", "Логотип GhostDeck" };
        m["icon_ghost_dark"] = new[] { "Ghost (dark)", "Duszek (ciemna)", "Geist (dunkel)", "Fantôme (sombre)", "Fantasma (oscuro)", "幽灵（深色）", "Fantasma (escuro)", "Призрак (тёмный)" };
        m["icon_ghost_light"] = new[] { "Ghost (light)", "Duszek (jasna)", "Geist (hell)", "Fantôme (clair)", "Fantasma (claro)", "幽灵（浅色）", "Fantasma (claro)", "Призрак (светлый)" };
        m["icon_gauge"]     = new[] { "Classic gauge", "Klasyczny zegar", "Klassische Anzeige", "Jauge classique", "Indicador clásico", "经典仪表", "Indicador clássico", "Классический спидометр" };
        m["icon_ghost_cyan"] = new[] { "Ghost (light, cyan)", "Duszek (jasna, cyan)", "Geist (hell, Cyan)", "Fantôme (clair, cyan)", "Fantasma (claro, cian)", "幽灵（浅色，青色）", "Fantasma (claro, ciano)", "Призрак (светлый, циан)" };
        m["scen_active"]    = new[] { "ACTIVE", "AKTYWNY", "AKTIV", "ACTIF", "ACTIVO", "当前", "ATIVO", "АКТИВЕН" };
        m["scen_select"]    = new[] { "SELECT", "WYBIERZ", "WÄHLEN", "CHOISIR", "ELEGIR", "选择", "SELECIONAR", "ВЫБРАТЬ" };
        m["rep_restart"]    = new[] { "Start over", "Zacznij od nowa", "Neu beginnen", "Recommencer", "Empezar de nuevo", "重新开始", "Recomeçar", "Начать заново" };
        m["set_grp_ui"]     = new[] { "Interface", "Interfejs", "Oberfläche", "Interface", "Interfaz", "界面", "Interface", "Интерфейс" };
        m["set_grid"]       = new[] { "Background grid", "Siatka w tle", "Hintergrundraster", "Grille d'arrière-plan", "Cuadrícula de fondo", "背景网格", "Grade de fundo", "Фоновая сетка" };
    }

    private static void L06(Dictionary<string, string[]> m)
    {
        m["set_tab_as_icon"] = new[] { "{0} — as an icon on the right", "{0} - jako ikona po prawej", "{0} — als Symbol rechts", "{0} — en icône à droite", "{0} — como icono a la derecha", "{0} — 显示为右侧图标", "{0} — como ícone à direita", "{0} — значком справа" };
        m["set_charge"]     = new[] { "Battery charge limit", "Limit ładowania baterii", "Akkuladelimit", "Limite de charge batterie", "Límite de carga", "电池充电限制", "Limite de carga", "Лимит заряда батареи" };
        m["charge_dont"]    = new[] { "Don't change", "Nie zmieniaj", "Nicht ändern", "Ne pas changer", "No cambiar", "不更改", "Não alterar", "Не менять" };
        m["set_autoswitch"] = new[] { "Auto-switch AC / battery", "Auto-przełączanie zasilacz / bateria", "Auto-Wechsel Netz / Akku", "Bascule auto secteur / batterie", "Cambio auto CA / batería", "电源/电池自动切换", "Troca auto tomada / bateria", "Автопереключение сеть / батарея" };
        m["on_ac"]          = new[] { "On AC", "Na zasilaczu", "Am Netz", "Sur secteur", "Con CA", "接通电源", "Na tomada", "От сети" };
        m["on_battery"]     = new[] { "On battery", "Na baterii", "Im Akku", "Sur batterie", "Con batería", "使用电池", "Na bateria", "От батареи" };

        m["status_title"]   = new[] { "Status / Diagnostics", "Status / Diagnostyka", "Status / Diagnose", "Statut / Diagnostic", "Estado / Diagnóstico", "状态 / 诊断", "Status / Diagnóstico", "Состояние / Диагностика" };
        m["st_profile"]     = new[] { "Active profile", "Aktywny profil", "Aktives Profil", "Profil actif", "Perfil activo", "当前配置", "Perfil ativo", "Активный профиль" };
        m["st_cpu_temp"]    = new[] { "CPU temperature", "Temperatura CPU", "CPU-Temperatur", "Température CPU", "Temperatura CPU", "CPU 温度", "Temperatura da CPU", "Температура ЦП" };
        m["st_gpu_temp"]    = new[] { "GPU temperature", "Temperatura GPU", "GPU-Temperatur", "Température GPU", "Temperatura GPU", "GPU 温度", "Temperatura da GPU", "Температура ГП" };
        m["st_cpu_fan"]     = new[] { "CPU fan", "Wentylator CPU", "CPU-Lüfter", "Ventilateur CPU", "Ventilador CPU", "CPU 风扇", "Ventilador da CPU", "Вентилятор ЦП" };
        m["st_gpu_fan"]     = new[] { "GPU fan", "Wentylator GPU", "GPU-Lüfter", "Ventilateur GPU", "Ventilador GPU", "GPU 风扇", "Ventilador da GPU", "Вентилятор ГП" };
        m["st_charge"]      = new[] { "Charge limit", "Limit ładowania", "Ladelimit", "Limite de charge", "Límite de carga", "充电限制", "Limite de carga", "Лимит заряда" };
        m["st_firmware"]    = new[] { "EC firmware", "Firmware EC", "EC-Firmware", "Firmware EC", "Firmware EC", "EC 固件", "Firmware EC", "Прошивка EC" };
        m["st_switches"]    = new[] { "Switches (session)", "Przełączeń (sesja)", "Wechsel (Sitzung)", "Changements (session)", "Cambios (sesión)", "切换次数（本次）", "Trocas (sessão)", "Переключений (сессия)" };
        m["st_in_profile"]  = new[] { "Time in profile", "Czas w profilu", "Zeit im Profil", "Temps dans le profil", "Tiempo en perfil", "当前配置时长", "Tempo no perfil", "Время в профиле" };
        m["st_autostart"]   = new[] { "Autostart", "Autostart", "Autostart", "Démarrage auto", "Inicio automático", "自动启动", "Início automático", "Автозапуск" };
        m["st_app_ver"]     = new[] { "App version", "Wersja aplikacji", "App-Version", "Version de l'app", "Versión de la app", "应用版本", "Versão do app", "Версия приложения" };
        m["st_cpu_clock"]   = new[] { "CPU clock (approx.)", "Zegar CPU (przybl.)", "CPU-Takt (ca.)", "Fréq. CPU (approx.)", "Reloj CPU (aprox.)", "CPU 频率(约)", "Clock CPU (aprox.)", "Частота CPU (прибл.)" };
        m["st_gpu_usage"]   = new[] { "GPU load", "Użycie GPU", "GPU-Last", "Charge GPU", "Carga GPU", "GPU 占用", "Carga GPU", "Загрузка GPU" };
        m["st_vram"]        = new[] { "VRAM used", "Użycie VRAM", "VRAM belegt", "VRAM utilisée", "VRAM usada", "显存占用", "VRAM usada", "Видеопамять" };
        m["st_battery"]     = new[] { "Battery", "Bateria", "Akku", "Batterie", "Batería", "电量", "Bateria", "Батарея" };
        m["st_refresh"]     = new[] { "Refresh", "Odśwież", "Aktualisieren", "Actualiser", "Actualizar", "刷新", "Atualizar", "Обновить" };
        m["always_on_top"]  = new[] { "Always on top", "Zawsze na wierzchu", "Immer im Vordergrund", "Toujours au-dessus", "Siempre visible", "总在最前", "Sempre no topo", "Поверх всех окон" };
        m["st_model"]       = new[] { "Model", "Model", "Modell", "Modèle", "Modelo", "型号", "Modelo", "Модель" };
        m["unsupported_title"] = new[] { "Unsupported model", "Niewspierany model", "Nicht unterstütztes Modell", "Modèle non pris en charge", "Modelo no compatible", "不支持的型号", "Modelo não suportado", "Модель не поддерживается" };
        m["unsupported_sub"]   = new[] { "read-only — contribute on GitHub", "tylko odczyt — zgłoś model na GitHub", "schreibgeschützt — auf GitHub beitragen", "lecture seule — contribuez sur GitHub", "solo lectura — contribuye en GitHub", "只读 — 在 GitHub 上贡献", "somente leitura — contribua no GitHub", "только чтение — добавьте на GitHub" };
        m["experimental_enable"] = new[] { "Enable experimental models (unverified)", "Włącz modele eksperymentalne (niezweryfikowane)", "Experimentelle Modelle aktivieren (ungeprüft)", "Activer les modèles expérimentaux (non vérifiés)", "Activar modelos experimentales (no verificados)", "启用实验性型号（未验证）", "Ativar modelos experimentais (não verificados)", "Включить экспериментальные модели (непроверенные)" };
        m["set_check_updates"] = new[] {
            "Check for updates (once a day)", "Sprawdzaj aktualizacje (raz dziennie)", "Auf Updates prüfen (täglich)",
            "Vérifier les mises à jour (une fois par jour)", "Buscar actualizaciones (una vez al día)",
            "检查更新（每天一次）", "Procurar atualizações (uma vez por dia)", "Проверять обновления (раз в день)" };
        m["update_available"] = new[] {
            "Update available", "Dostępna aktualizacja", "Update verfügbar", "Mise à jour disponible",
            "Actualización disponible", "有可用更新", "Atualização disponível", "Доступно обновление" };
        m["update_available_text"] = new[] {
            "Version {0} is available — click to download.", "Dostępna jest wersja {0} — kliknij, aby pobrać.",
            "Version {0} ist verfügbar – zum Herunterladen klicken.", "La version {0} est disponible — cliquez pour télécharger.",
            "La versión {0} está disponible — haz clic para descargar.", "新版本 {0} 可用 — 点击下载。",
            "A versão {0} está disponível — clique para baixar.", "Доступна версия {0} — нажмите, чтобы скачать." };
        m["menu_update"] = new[] {
            "⬇ Download new version", "⬇ Pobierz nową wersję", "⬇ Neue Version herunterladen",
            "⬇ Télécharger la nouvelle version", "⬇ Descargar nueva versión", "⬇ 下载新版本",
            "⬇ Baixar nova versão", "⬇ Скачать новую версию" };
        m["experimental_locked"] = new[] { "experimental — enable in Settings", "eksperymentalny — włącz w Ustawieniach", "experimentell — in Einstellungen aktivieren", "expérimental — activez dans Paramètres", "experimental — actívalo en Ajustes", "实验性 — 在设置中启用", "experimental — ative nas Configurações", "экспериментально — включите в настройках" };
        m["tier_experimental"]   = new[] { "experimental", "eksperymentalny", "experimentell", "expérimental", "experimental", "实验性", "experimental", "экспериментальный" };
        m["tier_tested"]         = new[] { "tested", "zweryfikowany", "getestet", "testé", "probado", "已测试", "testado", "проверено" };
        m["tier_unsupported"]    = new[] { "unsupported", "niewspierany", "nicht unterstützt", "non pris en charge", "no compatible", "不支持", "não suportado", "не поддерживается" };

        // ---- Report my model wizard ----
        m["menu_report"]    = new[] { "Report my model…", "Zgłoś mój model…", "Mein Modell melden…", "Signaler mon modèle…", "Reportar mi modelo…", "上报我的型号…", "Relatar meu modelo…", "Сообщить о модели…" };
        m["menu_feedback"]  = new[] { "Send feedback…", "Wyślij opinię…", "Feedback senden…", "Envoyer un avis…", "Enviar comentarios…", "发送反馈…", "Enviar feedback…", "Отправить отзыв…" };
        m["notice_more"]    = new[] { "Details", "Szczegóły", "Details", "Détails", "Detalles", "详情", "Detalhes", "Подробнее" };
        m["rep_title"]      = new[] { "Report my model", "Zgłoś mój model", "Mein Modell melden", "Signaler mon modèle", "Reportar mi modelo", "上报我的型号", "Relatar meu modelo", "Сообщить о модели" };
        m["rep_intro"]      = new[] {
            "Help add support for your laptop. This reads your EC in each MSI Center scenario (READ-ONLY — nothing is written) and prepares a GitHub report for you.",
            "Pomóż dodać wsparcie dla Twojego laptopa. Odczytamy EC w każdym scenariuszu MSI Center (TYLKO ODCZYT — nic nie jest zapisywane) i przygotujemy zgłoszenie na GitHub.",
            "Hilf, Unterstützung für dein Gerät hinzuzufügen. Liest den EC in jedem MSI-Center-Szenario (NUR LESEN — nichts wird geschrieben) und erstellt einen GitHub-Bericht.",
            "Aidez à prendre en charge votre PC. Lit l'EC dans chaque scénario MSI Center (LECTURE SEULE — rien n'est écrit) et prépare un rapport GitHub.",
            "Ayuda a añadir soporte para tu portátil. Lee el EC en cada escenario de MSI Center (SOLO LECTURA — no se escribe nada) y prepara un informe de GitHub.",
            "帮助为你的笔记本添加支持。将在每个 MSI Center 场景下读取 EC（只读——不写入任何内容）并为你准备 GitHub 报告。",
            "Ajude a adicionar suporte ao seu notebook. Lê o EC em cada cenário do MSI Center (SOMENTE LEITURA — nada é gravado) e prepara um relatório no GitHub.",
            "Помогите добавить поддержку вашего ноутбука. Считывает EC в каждом сценарии MSI Center (ТОЛЬКО ЧТЕНИЕ — ничего не записывается) и готовит отчёт на GitHub." };
        m["rep_need_msi"]   = new[] {
            "Requires MSI Center installed (to set each scenario as a reference).",
            "Wymaga zainstalowanego MSI Center (do ustawienia każdego scenariusza jako wzorca).",
            "Erfordert installiertes MSI Center (zum Setzen jedes Szenarios als Referenz).",
            "Nécessite MSI Center installé (pour définir chaque scénario comme référence).",
            "Requiere MSI Center instalado (para fijar cada escenario como referencia).",
            "需要已安装 MSI Center（用于将每个场景设为参考）。",
            "Requer o MSI Center instalado (para definir cada cenário como referência).",
            "Требуется установленный MSI Center (чтобы задать каждый сценарий как эталон)." };
        m["rep_msi_tip"]    = new[] {
            "Best with MSI Center 2.0.48 — the last version with a working SILENT scenario. Newer versions auto-update and silently drop SILENT after a reboot (exactly why this app exists).",
            "Najlepiej mieć MSI Center 2.0.48 — ostatnią wersję z działającym scenariuszem SILENT. Nowsze wersje same się aktualizują i po restarcie tracą tryb SILENT (właśnie dlatego powstała ta aplikacja).",
            "Am besten mit MSI Center 2.0.48 — der letzten Version mit funktionierendem SILENT-Szenario. Neuere Versionen aktualisieren sich selbst und verlieren SILENT nach einem Neustart (genau deshalb gibt es diese App).",
            "De préférence MSI Center 2.0.48 — la dernière version avec un scénario SILENT fonctionnel. Les versions plus récentes se mettent à jour seules et perdent SILENT après un redémarrage (la raison d'être de cette app).",
            "Mejor con MSI Center 2.0.48 — la última versión con el escenario SILENT funcional. Las versiones nuevas se autoactualizan y pierden SILENT tras reiniciar (justo por eso existe esta app).",
            "最好使用 MSI Center 2.0.48——最后一个 SILENT 场景可用的版本。较新版本会自动更新，重启后悄悄失去 SILENT（这正是本应用存在的原因）。",
            "Melhor com o MSI Center 2.0.48 — a última versão com o cenário SILENT funcionando. Versões mais novas se atualizam sozinhas e perdem o SILENT após reiniciar (exatamente por isso este app existe).",
            "Лучше всего MSI Center 2.0.48 — последняя версия с рабочим сценарием SILENT. Новые версии сами обновляются и теряют SILENT после перезагрузки (именно поэтому появилось это приложение)." };
        m["rep_msi_clean"]  = new[] {
            "Before installing 2.0.48, fully remove the current MSI Center with MSI's official cleaner:",
            "Przed instalacją 2.0.48 usuń całkowicie obecny MSI Center oficjalnym narzędziem MSI:",
            "Vor der Installation von 2.0.48 das aktuelle MSI Center mit dem offiziellen MSI-Cleaner vollständig entfernen:",
            "Avant d'installer 2.0.48, supprimez complètement le MSI Center actuel avec l'outil officiel MSI :",
            "Antes de instalar 2.0.48, elimina por completo el MSI Center actual con la herramienta oficial de MSI:",
            "安装 2.0.48 之前，请用 MSI 官方清理工具彻底卸载当前的 MSI Center：",
            "Antes de instalar o 2.0.48, remova completamente o MSI Center atual com a ferramenta oficial da MSI:",
            "Перед установкой 2.0.48 полностью удалите текущий MSI Center официальной утилитой MSI:" };
        m["rep_msi_download"] = new[] {
            "Get MSI Center 2.0.48 from Uptodown. Use the direct link; if it ever stops working, use the full version list as a fallback:",
            "Pobierz MSI Center 2.0.48 z Uptodown. Użyj linku bezpośredniego; gdyby przestał działać, skorzystaj z pełnej listy wersji jako zapasowej:",
            "MSI Center 2.0.48 von Uptodown laden. Direktlink verwenden; falls er nicht mehr funktioniert, die vollständige Versionsliste als Ausweichoption nutzen:",
            "Téléchargez MSI Center 2.0.48 depuis Uptodown. Utilisez le lien direct ; s'il cesse de fonctionner, utilisez la liste complète des versions en secours :",
            "Descarga MSI Center 2.0.48 desde Uptodown. Usa el enlace directo; si deja de funcionar, usa la lista completa de versiones como alternativa:",
            "从 Uptodown 获取 MSI Center 2.0.48。请使用直链；若失效，可改用完整版本列表作为备用：",
            "Baixe o MSI Center 2.0.48 no Uptodown. Use o link direto; se parar de funcionar, use a lista completa de versões como alternativa:",
            "Скачайте MSI Center 2.0.48 с Uptodown. Используйте прямую ссылку; если она перестанет работать, используйте полный список версий как запасной вариант:" };
    }

    private static void L07(Dictionary<string, string[]> m)
    {
        m["rep_dl_version"] = new[] {
            "Download MSI Center 2.0.48 (direct link)",
            "Pobierz MSI Center 2.0.48 (link bezpośredni)",
            "MSI Center 2.0.48 herunterladen (Direktlink)",
            "Télécharger MSI Center 2.0.48 (lien direct)",
            "Descargar MSI Center 2.0.48 (enlace directo)",
            "下载 MSI Center 2.0.48（直链）",
            "Baixar MSI Center 2.0.48 (link direto)",
            "Скачать MSI Center 2.0.48 (прямая ссылка)" };
        m["rep_dl_repo"] = new[] {
            "All MSI Center versions on Uptodown (fallback)",
            "Wszystkie wersje MSI Center na Uptodown (zapasowo)",
            "Alle MSI-Center-Versionen auf Uptodown (Ausweich)",
            "Toutes les versions de MSI Center sur Uptodown (secours)",
            "Todas las versiones de MSI Center en Uptodown (alternativa)",
            "Uptodown 上的所有 MSI Center 版本（备用）",
            "Todas as versões do MSI Center no Uptodown (alternativa)",
            "Все версии MSI Center на Uptodown (запасной вариант)" };
        m["rep_uninstaller_link"] = new[] {
            "Download CleanCenterMaster (official MSI uninstaller)",
            "Pobierz CleanCenterMaster (oficjalny deinstalator MSI)",
            "CleanCenterMaster herunterladen (offizieller MSI-Deinstaller)",
            "Télécharger CleanCenterMaster (désinstalleur officiel MSI)",
            "Descargar CleanCenterMaster (desinstalador oficial de MSI)",
            "下载 CleanCenterMaster（MSI 官方卸载工具）",
            "Baixar CleanCenterMaster (desinstalador oficial da MSI)",
            "Скачать CleanCenterMaster (официальный деинсталлятор MSI)" };
        m["rep_section"]    = new[] { "EC CAPTURE", "PRZECHWYTYWANIE EC", "EC-ERFASSUNG", "CAPTURE EC", "CAPTURA EC", "EC 采集", "CAPTURA DO EC", "СНЯТИЕ EC" };
        m["st_cpu_usage"]   = new[] { "CPU usage", "Użycie CPU", "CPU-Last", "Charge CPU", "Uso de CPU", "CPU 占用", "Uso da CPU", "Загрузка CPU" };
        m["st_ram"]         = new[] { "RAM", "RAM", "RAM", "RAM", "RAM", "内存", "RAM", "ОЗУ" };
        m["test_tools"]     = new[] { "Test tools (advanced)", "Narzędzia testowe (zaawansowane)", "Testwerkzeuge (erweitert)", "Outils de test (avancé)", "Herramientas de prueba", "测试工具（高级）", "Ferramentas de teste", "Тест-инструменты" };
        m["test_title"]     = new[] { "Test tools", "Narzędzia testowe", "Testwerkzeuge", "Outils de test", "Herramientas de prueba", "测试工具", "Ferramentas de teste", "Тест-инструменты" };
        m["test_rpm_btn"]   = new[] { "Scan for fan RPM register", "Skanuj rejestr RPM wentylatora", "Lüfter-RPM-Register suchen", "Chercher le registre RPM", "Buscar registro RPM", "扫描风扇 RPM 寄存器", "Procurar registo de RPM", "Поиск регистра RPM" };
        m["test_rpm_hint"]  = new[] {
            "Read-only. Match a value below to the RPM shown in MSI Center, then tell me the address.",
            "Tylko odczyt. Dopasuj wartość poniżej do RPM z MSI Center i podaj mi adres.",
            "Nur Lesen. Ordne einen Wert dem RPM in MSI Center zu und nenne mir die Adresse.",
            "Lecture seule. Associez une valeur au RPM de MSI Center, puis donnez-moi l'adresse.",
            "Solo lectura. Asocia un valor al RPM de MSI Center y dime la dirección.",
            "只读。把下面的值与 MSI Center 的 RPM 对应，然后告诉我地址。",
            "Somente leitura. Associe um valor ao RPM do MSI Center e me diga o endereço.",
            "Только чтение. Сопоставьте значение с RPM в MSI Center и сообщите адрес." };
        m["test_rpm_a"]     = new[] { "Step 1: capture current speed", "Krok 1: zapisz obecne obroty", "Schritt 1: aktuelle Drehzahl", "Étape 1 : vitesse actuelle", "Paso 1: velocidad actual", "步骤 1：记录当前转速", "Passo 1: velocidade atual", "Шаг 1: текущие обороты" };
        m["test_rpm_b"]     = new[] { "Step 2: scan + compare", "Krok 2: skanuj i porównaj", "Schritt 2: scannen + vergleichen", "Étape 2 : scanner + comparer", "Paso 2: escanear y comparar", "步骤 2：扫描并比较", "Passo 2: escanear e comparar", "Шаг 2: сканировать и сравнить" };
        m["test_rpm_hint2"] = new[] {
            "Run step 1, then change fan speed (use the experiment below), then step 2. Addresses whose value changed are the tachometers — match to MSI Center.",
            "Zrób krok 1, potem zmień obroty (użyj eksperymentu poniżej), potem krok 2. Adresy, które się zmieniły, to tachometry — dopasuj do MSI Center.",
            "Schritt 1, dann Drehzahl ändern (Experiment unten), dann Schritt 2. Geänderte Adressen sind die Tachos — mit MSI Center abgleichen.",
            "Étape 1, changez la vitesse (expérience ci-dessous), puis étape 2. Les adresses modifiées sont les tachymètres — comparez à MSI Center.",
            "Paso 1, cambia la velocidad (experimento abajo), luego paso 2. Las direcciones que cambiaron son los tacómetros — compara con MSI Center.",
            "先步骤 1，改变转速（用下面的实验），再步骤 2。发生变化的地址就是转速寄存器——与 MSI Center 对照。",
            "Passo 1, mude a velocidade (experimento abaixo), depois passo 2. Os endereços que mudaram são os tacômetros — compare com o MSI Center.",
            "Шаг 1, измените обороты (эксперимент ниже), затем шаг 2. Изменившиеся адреса — это тахометры, сверьте с MSI Center." };
        m["test_dump_btn"]  = new[] { "Save EC dump to file", "Zapisz zrzut EC do pliku", "EC-Dump speichern", "Enregistrer le dump EC", "Guardar volcado EC", "保存 EC 转储到文件", "Salvar dump do EC", "Сохранить дамп EC" };
        m["test_live"]      = new[] { "Live RPM:", "RPM na żywo:", "Live-RPM:", "RPM en direct :", "RPM en vivo:", "实时转速：", "RPM ao vivo:", "Обороты вживую:" };
        m["tab_fancurve"]   = new[] { "Fan curve", "Krzywa wentylatora", "Lüfterkurve", "Courbe ventilateur", "Curva ventilador", "风扇曲线", "Curva da ventoinha", "Кривая вентилятора" };
        m["fc_title"]       = new[] { "Fan curve", "Krzywa wentylatora", "Lüfterkurve", "Courbe du ventilateur", "Curva del ventilador", "风扇曲线", "Curva da ventoinha", "Кривая вентилятора" };
        m["fc_hint"]        = new[] {
            "Drag the points up/down to set fan speed per temperature. Apply writes the curve and runs it in the current power mode.",
            "Przeciągaj punkty góra/dół, aby ustawić obroty dla danej temperatury. „Zastosuj” zapisuje krzywą i uruchamia ją w aktualnym trybie.",
            "Punkte hoch/runter ziehen, um die Drehzahl je Temperatur zu setzen. „Anwenden” nutzt die Kurve im aktuellen Modus.",
            "Glissez les points pour régler la vitesse par température. « Appliquer » utilise la courbe dans le mode actuel.",
            "Arrastra los puntos para fijar la velocidad por temperatura. «Aplicar» usa la curva en el modo actual.",
            "上下拖动各点以设置对应温度的转速。“应用”会在当前模式下使用该曲线。",
            "Arraste os pontos para definir a velocidade por temperatura. “Aplicar” usa a curva no modo atual.",
            "Перетаскивайте точки, чтобы задать обороты для температуры. «Применить» включает кривую в текущем режиме." };
        m["fc_locked"]      = new[] {
            "Editing is available only on supported (Tested / enabled experimental) models.",
            "Edycja dostępna tylko na obsługiwanych modelach (Tested / włączone eksperymentalne).",
            "Bearbeitung nur auf unterstützten Modellen (Tested / aktiviert experimentell).",
            "Édition disponible uniquement sur les modèles pris en charge (Tested / expérimental activé).",
            "La edición solo está disponible en modelos compatibles (Tested / experimental habilitado).",
            "仅支持的机型（Tested / 已启用实验性）可编辑。",
            "Edição disponível apenas em modelos suportados (Tested / experimental ativado).",
            "Редактирование доступно только на поддерживаемых моделях (Tested / включён эксперимент)." };
        m["fc_fan_cpu"]     = new[] { "Fan 1 (CPU)", "Wentylator 1 (CPU)", "Lüfter 1 (CPU)", "Ventilateur 1 (CPU)", "Ventilador 1 (CPU)", "风扇 1（CPU）", "Ventoinha 1 (CPU)", "Вентилятор 1 (CPU)" };
        m["fc_fan_gpu"]     = new[] { "Fan 2 (GPU)", "Wentylator 2 (GPU)", "Lüfter 2 (GPU)", "Ventilateur 2 (GPU)", "Ventilador 2 (GPU)", "风扇 2（GPU）", "Ventoinha 2 (GPU)", "Вентилятор 2 (GPU)" };
        m["fc_fan_single"]  = new[] { "Fan (CPU)", "Wentylator (CPU)", "Lüfter (CPU)", "Ventilateur (CPU)", "Ventilador (CPU)", "风扇（CPU）", "Ventoinha (CPU)", "Вентилятор (CPU)" };
        m["fc_single_note"] = new[]
        {
            "This model has a single controllable fan curve",
            "Ten model ma jedną sterowalną krzywą wentylatora",
            "Dieses Modell hat eine einzige steuerbare Lüfterkurve",
            "Ce modèle a une seule courbe de ventilateur contrôlable",
            "Este modelo tiene una sola curva de ventilador controlable",
            "该机型只有一条可控风扇曲线",
            "Este modelo tem uma única curva de ventoinha controlável",
            "У этой модели одна управляемая кривая вентилятора"
        };
        m["fc_apply"]       = new[] { "Apply", "Zastosuj", "Anwenden", "Appliquer", "Aplicar", "应用", "Aplicar", "Применить" };
        m["fc_restore"]     = new[] { "Restore automatic fans", "Przywróć automatyczne", "Automatik wiederherstellen", "Rétablir l'automatique", "Restaurar automático", "恢复自动", "Restaurar automático", "Вернуть авто" };
        m["fc_enable"]      = new[] { "Custom fan curve (override this profile)", "Własna krzywa wentylatora (nadpisz ten profil)", "Eigene Lüfterkurve (Profil überschreiben)", "Courbe perso (remplacer ce profil)", "Curva personalizada (anular este perfil)", "自定义风扇曲线（覆盖此模式）", "Curva personalizada (substituir este perfil)", "Своя кривая (заменить профиль)" };
        m["st_matrix"]      = new[] { "Profile bytes (EC)", "Bajty profilu (EC)", "Profil-Bytes (EC)", "Octets de profil (EC)", "Bytes de perfil (EC)", "配置字节 (EC)", "Bytes do perfil (EC)", "Байты профиля (EC)" };
        m["st_now"]         = new[] { "Now (live)", "Teraz (na żywo)", "Jetzt (live)", "Maintenant", "Ahora", "当前（实时）", "Agora", "Сейчас" };
        m["st_b_byte"]      = new[] { "Byte", "Bajt", "Byte", "Octet", "Byte", "字节", "Byte", "Байт" };
        m["st_b_role"]      = new[] { "Controls", "Za co odpowiada", "Steuert", "Contrôle", "Controla", "作用", "Controla", "Отвечает за" };
        m["st_b_vals"]      = new[] { "Values", "Wartości", "Werte", "Valeurs", "Valores", "取值", "Valores", "Значения" };
        m["st_b_power"]     = new[] { "Power / performance", "Moc / wydajność", "Leistung", "Puissance", "Potencia", "性能", "Desempenho", "Мощность" };
        m["st_b_cap"]       = new[] { "Extreme power", "Moc Extreme", "Extreme-Leistung", "Puissance Extreme", "Potencia Extreme", "Extreme 功耗", "Potência Extreme", "Мощность Extreme" };
        m["st_b_others"]    = new[] { "others", "reszta", "andere", "autres", "resto", "其余", "outros", "остальные" };
        m["st_b_batt"]      = new[] { "Super battery", "Super-bateria", "Super-Akku", "Super batterie", "Súper batería", "超级省电", "Super bateria", "Супер-батарея" };
        m["st_b_fan"]       = new[] { "Fan", "Wentylator", "Lüfter", "Ventilateur", "Ventilador", "风扇", "Ventoinha", "Вентилятор" };
        m["st_curve_live"]  = new[] { "Fan curve tables (live)", "Tablice krzywej (na żywo)", "Lüfterkurven-Tabellen (live)", "Tables de courbe (live)", "Tablas de curva (vivo)", "风扇曲线表（实时）", "Tabelas da curva (ao vivo)", "Таблицы кривой (вживую)" };
        m["st_point"]       = new[] { "Point", "Punkt", "Punkt", "Point", "Punto", "点", "Ponto", "Точка" };
        m["st_fan_silent"]  = new[] { "silent", "cicho", "leise", "silencieux", "silencio", "静音", "silencioso", "тихо" };
        m["st_fan_auto"]    = new[] { "auto", "auto", "auto", "auto", "auto", "自动", "auto", "авто" };
        m["st_fan_curve"]   = new[] { "curve", "krzywa", "Kurve", "courbe", "curva", "曲线", "curva", "кривая" };
        m["st_v_comfort"]   = new[] { "comfort", "komfort", "Komfort", "confort", "confort", "舒适", "conforto", "комфорт" };
        m["st_v_turbo"]     = new[] { "turbo", "turbo", "Turbo", "turbo", "turbo", "极速", "turbo", "турбо" };
        m["st_v_eco"]       = new[] { "eco", "eco", "Eco", "éco", "eco", "节能", "eco", "эко" };
        m["st_on"]          = new[] { "on", "wł", "an", "activé", "act.", "开", "lig.", "вкл" };
    }

    private static void L08(Dictionary<string, string[]> m)
    {
        m["st_off"]         = new[] { "off", "wył", "aus", "désact.", "desact.", "关", "des.", "выкл" };
        m["fc_preview"]     = new[] {
            "Curve addresses are not verified on this model yet — check the preview matches MSI Center (Extreme → Advanced). You can revert any time.",
            "Adresy krzywej nie są jeszcze zweryfikowane na tym modelu — sprawdź, czy podgląd zgadza się z MSI Center (Extreme → Advanced). Możesz cofnąć w każdej chwili.",
            "Kurvenadressen für dieses Modell noch nicht geprüft — prüfe, ob die Vorschau MSI Center (Extreme → Advanced) entspricht. Jederzeit rückgängig.",
            "Adresses de courbe non vérifiées sur ce modèle — vérifiez que l'aperçu correspond à MSI Center (Extreme → Advanced). Réversible à tout moment.",
            "Direcciones de curva sin verificar en este modelo — comprueba que la vista coincide con MSI Center (Extreme → Advanced). Reversible.",
            "此型号的曲线地址尚未验证——请确认预览与 MSI Center（Extreme → Advanced）一致。可随时还原。",
            "Endereços da curva ainda não verificados neste modelo — confira se a prévia bate com o MSI Center (Extreme → Advanced). Reversível.",
            "Адреса кривой на этой модели не проверены — сверьте предпросмотр с MSI Center (Extreme → Advanced). Можно вернуть в любой момент." };
        m["st_matrix_note"] = new[] {
            "0x34 = Extreme power unlock (written 00 in Extreme, 01 elsewhere), but it reads dynamically and can show 00/01 in any comfort profile — so it is NOT used for detection. Silent vs Balanced is decided solely by the fan byte 0xD4 (1D = Silent).",
            "0x34 = odblokowanie mocy Extreme (zapisywane 00 w Extreme, 01 w pozostałych), ale odczytuje się dynamicznie i bywa 00/01 w każdym profilu komfort — dlatego NIE służy do detekcji. O Silent vs Balanced decyduje wyłącznie bajt wentylatora 0xD4 (1D = Silent).",
            "0x34 = Extreme-Leistungsfreigabe (00 in Extreme, sonst 01 geschrieben), liest sich aber dynamisch und kann in jedem Komfortprofil 00/01 zeigen — daher NICHT zur Erkennung. Silent vs Balanced entscheidet allein das Lüfterbyte 0xD4 (1D = Silent).",
            "0x34 = déblocage puissance Extreme (écrit 00 en Extreme, 01 ailleurs), mais sa lecture est dynamique et peut afficher 00/01 dans tout profil confort — donc PAS utilisé pour la détection. Silent vs Balanced dépend uniquement de l'octet 0xD4 (1D = Silent).",
            "0x34 = desbloqueo de potencia Extreme (se escribe 00 en Extreme, 01 en el resto), pero su lectura es dinámica y puede mostrar 00/01 en cualquier perfil confort — por eso NO se usa para detección. Silent vs Balanced lo decide solo el byte 0xD4 (1D = Silent).",
            "0x34 = Extreme 功耗解锁（Extreme 写 00，其余写 01），但读取是动态的，任何舒适档都可能显示 00/01——故不用于识别。Silent 与 Balanced 仅由风扇字节 0xD4 区分（1D = Silent）。",
            "0x34 = desbloqueio de potência Extreme (gravado 00 no Extreme, 01 nos outros), mas a leitura é dinâmica e pode mostrar 00/01 em qualquer perfil conforto — por isso NÃO é usado na detecção. Silent vs Balanced é decidido só pelo byte 0xD4 (1D = Silent).",
            "0x34 = разблокировка мощности Extreme (пишется 00 в Extreme, 01 в остальных), но читается динамически и может показывать 00/01 в любом комфортном профиле — поэтому НЕ используется для определения. Silent и Balanced различает только байт 0xD4 (1D = Silent)." };
        m["fc_silent_warn"] = new[] {
            "Silent's power cap lives in the same byte as the fan curve (0xD4), so a custom curve turns Silent off — the laptop runs at Balanced power with your fans. Continue?",
            "Limit mocy Silenta siedzi w tym samym bajcie co krzywa (0xD4), więc własna krzywa wyłącza Silent — laptop pójdzie na mocy Balanced z Twoimi wentylatorami. Kontynuować?",
            "Das Power-Limit von Silent liegt im selben Byte wie die Kurve (0xD4) — eine eigene Kurve schaltet Silent ab; der Laptop läuft mit Balanced-Leistung. Fortfahren?",
            "Le plafond de puissance Silent est dans le même octet que la courbe (0xD4) ; une courbe désactive Silent — puissance Balanced avec vos ventilateurs. Continuer ?",
            "El límite de potencia de Silent está en el mismo byte que la curva (0xD4); una curva desactiva Silent — potencia Balanced con tus ventiladores. ¿Continuar?",
            "Silent 的功耗上限与风扇曲线在同一字节（0xD4），自定义曲线会关闭 Silent——将以 Balanced 功耗运行。继续？",
            "O limite de potência do Silent está no mesmo byte da curva (0xD4); uma curva desliga o Silent — potência Balanced com suas ventoinhas. Continuar?",
            "Лимит мощности Silent в том же байте, что и кривая (0xD4); своя кривая отключает Silent — ноутбук работает на мощности Balanced. Продолжить?" };
        m["fc_mode"]        = new[] { "Fan mode:", "Tryb wentylatora:", "Lüftermodus:", "Mode ventilateur :", "Modo ventilador:", "风扇模式：", "Modo da ventoinha:", "Режим вентилятора:" };
        m["fc_default"]     = new[] { "MSI default", "MSI domyślny", "MSI-Standard", "MSI par défaut", "MSI predeterminado", "MSI 默认", "Padrão MSI", "По умолчанию MSI" };
        m["fc_applied"]     = new[] { "Custom fan curve applied to the current mode.", "Własna krzywa zastosowana w aktualnym trybie.", "Eigene Lüfterkurve im aktuellen Modus angewendet.", "Courbe personnalisée appliquée au mode actuel.", "Curva personalizada aplicada al modo actual.", "已在当前模式应用自定义曲线。", "Curva personalizada aplicada ao modo atual.", "Своя кривая применена в текущем режиме." };
        m["fc_warn_low"]    = new[] {
            "The highest-temperature speed is low — fans may stay quiet under heavy load and the laptop can get hot. Apply anyway?",
            "Prędkość przy najwyższej temperaturze jest niska — pod dużym obciążeniem wentylatory mogą zostać ciche i laptop się zagrzeje. Zastosować mimo to?",
            "Die Drehzahl bei höchster Temperatur ist niedrig — unter Last können die Lüfter leise bleiben und das Gerät heiß werden. Trotzdem anwenden?",
            "La vitesse à la température max est faible — sous charge, les ventilateurs peuvent rester silencieux et le PC chauffer. Appliquer quand même ?",
            "La velocidad a la temperatura máxima es baja — con carga, los ventiladores pueden quedar silenciosos y el equipo calentarse. ¿Aplicar igual?",
            "最高温度下的转速偏低——重载时风扇可能仍很安静、机器会发热。仍要应用吗？",
            "A velocidade na temperatura máxima é baixa — sob carga, as ventoinhas podem ficar silenciosas e o aparelho esquentar. Aplicar mesmo assim?",
            "Скорость при макс. температуре низкая — под нагрузкой вентиляторы могут остаться тихими, и ноутбук нагреется. Всё равно применить?" };
        m["test_curve_btn"] = new[] { "Show fan curve (read-only)", "Pokaż krzywą wentylatora (read-only)", "Lüfterkurve anzeigen (nur Lesen)", "Afficher la courbe (lecture seule)", "Mostrar curva (solo lectura)", "显示风扇曲线（只读）", "Mostrar curva (somente leitura)", "Показать кривую (только чтение)" };
        m["test_curve_none"]= new[] { "No fan-curve map for this model yet.", "Brak mapy krzywej dla tego modelu.", "Noch keine Lüfterkurven-Map für dieses Modell.", "Pas encore de courbe pour ce modèle.", "Aún no hay mapa de curva para este modelo.", "该型号尚无风扇曲线映射。", "Ainda não há mapa de curva para este modelo.", "Для этой модели пока нет карты кривой." };
        m["test_curve_hint"]= new[] {
            "Read-only preview — compare with MSI Center (Extreme → Advanced) to confirm the point count and values.",
            "Podgląd tylko do odczytu — porównaj z MSI Center (Extreme → Advanced), aby potwierdzić liczbę punktów i wartości.",
            "Nur-Lese-Vorschau — mit MSI Center (Extreme → Advanced) vergleichen, um Punktanzahl und Werte zu bestätigen.",
            "Aperçu en lecture seule — comparez avec MSI Center (Extreme → Advanced) pour confirmer le nombre de points et les valeurs.",
            "Vista previa de solo lectura — compara con MSI Center (Extreme → Advanced) para confirmar el número de puntos y los valores.",
            "只读预览——与 MSI Center（Extreme → Advanced）对比以确认点数和数值。",
            "Pré-visualização somente leitura — compare com o MSI Center (Extreme → Advanced) para confirmar o número de pontos e valores.",
            "Предпросмотр только для чтения — сравните с MSI Center (Extreme → Advanced), чтобы подтвердить число точек и значения." };
        m["test_dump_saved"]= new[] { "Saved to:\n{0}", "Zapisano do:\n{0}", "Gespeichert:\n{0}", "Enregistré :\n{0}", "Guardado en:\n{0}", "已保存到：\n{0}", "Salvo em:\n{0}", "Сохранено:\n{0}" };
        m["test_adv_on"]    = new[] { "Silent + Advanced fan (0xD4=0x8D)", "Silent + Advanced (0xD4=0x8D)", "Silent + Advanced (0xD4=0x8D)", "Silent + Advanced (0xD4=0x8D)", "Silent + Advanced (0xD4=0x8D)", "Silent + 高级 (0xD4=0x8D)", "Silent + Advanced (0xD4=0x8D)", "Silent + Advanced (0xD4=0x8D)" };
        m["test_adv_off"]   = new[] { "Restore Silent", "Przywróć Silent", "Silent wiederherstellen", "Restaurer Silent", "Restaurar Silent", "恢复 Silent", "Restaurar Silent", "Вернуть Silent" };
        m["test_note"]      = new[] {
            "Experiment: does the EC obey advanced fan control outside Extreme? Apply, watch the fans, then restore. Tested models only.",
            "Eksperyment: czy EC słucha trybu Advanced poza Extreme? Włącz, obserwuj wentylatory, potem przywróć. Tylko modele Tested.",
            "Experiment: gehorcht der EC dem Advanced-Modus außerhalb Extreme? Anwenden, Lüfter beobachten, dann zurücksetzen. Nur getestete Modelle.",
            "Expérience : l'EC obéit-il au mode Advanced hors Extreme ? Appliquez, observez les ventilateurs, puis restaurez. Modèles testés uniquement.",
            "Experimento: ¿obedece el EC el modo Advanced fuera de Extreme? Aplica, observa los ventiladores y restaura. Solo modelos probados.",
            "实验：在 Extreme 之外 EC 是否服从高级风扇控制？应用、观察风扇，然后恢复。仅限已测试机型。",
            "Experimento: o EC obedece ao modo Advanced fora do Extreme? Aplique, observe as ventoinhas e restaure. Apenas modelos testados.",
            "Эксперимент: слушается ли EC режима Advanced вне Extreme? Примените, посмотрите на вентиляторы, затем верните. Только проверенные модели." };
        m["tab_report"]     = new[] { "Report", "Zgłoś", "Melden", "Signaler", "Reportar", "提交机型", "Reportar", "Сообщить" };
        // ---- Models tab ----
        m["tab_models"]     = new[] { "Models", "Modele", "Modelle", "Modèles", "Modelos", "机型", "Modelos", "Модели" };
        m["mdl_intro"]      = new[] {
            "Every firmware ID the app recognises: {0} tested on real hardware, the rest experimental (opt-in) from the msi-ec / MControlCenter register maps. On an unrecognised firmware the app stays read-only.",
            "Wszystkie identyfikatory firmware rozpoznawane przez aplikację: {0} zweryfikowanych na sprzęcie, reszta eksperymentalna (opcjonalna) z map rejestrów msi-ec / MControlCenter. Nieznany firmware = tryb tylko do odczytu.",
            "Alle von der App erkannten Firmware-IDs: {0} auf echter Hardware getestet, der Rest experimentell (Opt-in) aus den msi-ec-/MControlCenter-Registerkarten. Bei unbekannter Firmware bleibt die App im Nur-Lese-Modus.",
            "Tous les identifiants de firmware reconnus : {0} testés sur du matériel réel, le reste expérimental (opt-in) d'après les cartes de registres msi-ec / MControlCenter. Firmware inconnu = lecture seule.",
            "Todos los ID de firmware reconocidos: {0} probados en hardware real, el resto experimental (opcional) según los mapas de registros de msi-ec / MControlCenter. Con firmware desconocido la app queda en solo lectura.",
            "应用识别的所有固件 ID：{0} 个已在真实硬件上测试，其余为实验性（需自行启用），来自 msi-ec / MControlCenter 寄存器映射。固件未识别时应用保持只读。",
            "Todos os IDs de firmware reconhecidos: {0} testados em hardware real, o resto experimental (opt-in) dos mapas de registros do msi-ec / MControlCenter. Com firmware desconhecido o app fica somente leitura.",
            "Все распознаваемые ID прошивок: {0} проверено на реальном железе, остальные экспериментальные (по желанию) из карт регистров msi-ec / MControlCenter. При неизвестной прошивке приложение остаётся в режиме только чтения." };
        m["mdl_you"]        = new[] { "Your model", "Twój model", "Dein Modell", "Votre modèle", "Tu modelo", "你的机型", "Seu modelo", "Ваша модель" };
        m["mdl_unknown"]    = new[] { "Firmware {0} not recognised — read-only mode.", "Firmware {0} nierozpoznany — tryb tylko do odczytu.", "Firmware {0} nicht erkannt — Nur-Lese-Modus.", "Firmware {0} non reconnu — mode lecture seule.", "Firmware {0} no reconocido — modo de solo lectura.", "未识别固件 {0} — 只读模式。", "Firmware {0} não reconhecido — modo somente leitura.", "Прошивка {0} не распознана — режим только чтения." };
        m["mdl_c_family"]   = new[] { "Family", "Rodzina", "Familie", "Famille", "Familia", "家族", "Família", "Семейство" };
        m["mdl_c_status"]   = new[] { "Status", "Status", "Status", "Statut", "Estado", "状态", "Status", "Статус" };
        m["mdl_c_curve"]    = new[] { "Fan curve", "Krzywa", "Lüfterkurve", "Courbe", "Curva", "风扇曲线", "Curva", "Кривая" };
        m["mdl_c_sb"]       = new[] { "SB", "SB", "SB", "SB", "SB", "SB", "SB", "SB" };
        m["mdl_c_rpm"]      = new[] { "RPM", "RPM", "RPM", "RPM", "RPM", "RPM", "RPM", "RPM" };
        m["mdl_curve_edit"] = new[] { "editable", "edytowalna", "editierbar", "modifiable", "editable", "可编辑", "editável", "редактируемая" };
        m["mdl_curve_prev"] = new[] { "unverified", "niezweryfikowany", "unbestätigt", "non vérifiée", "sin verificar", "未验证", "não verificada", "не проверена" };
        m["mdl_sb_tip"]     = new[] {
            "Super Battery\nWhether the model has a dedicated super-battery\nregister (deepest power/battery throttle).\nAffects the Super Battery profile.",
            "Super Battery\nCzy model ma dedykowany rejestr super-baterii\n(najgłębszy throttle mocy/baterii).\nWpływa na profil Super Battery.",
            "Super Battery\nOb das Modell ein eigenes Super-Battery-Register hat\n(tiefste Energie-/Akku-Drosselung).\nBetrifft das Super-Battery-Profil.",
            "Super Battery\nSi le modèle possède un registre super-batterie dédié\n(la limitation la plus profonde).\nConcerne le profil Super Battery.",
            "Super Battery\nSi el modelo tiene un registro de super-batería dedicado\n(la limitación más profunda).\nAfecta al perfil Super Battery.",
            "Super Battery\n机型是否有专用的超级省电寄存器\n（最深度的功耗/电池限制）。\n影响 Super Battery 配置文件。",
            "Super Battery\nSe o modelo tem um registro dedicado de super-bateria\n(a limitação mais profunda).\nAfeta o perfil Super Battery.",
            "Super Battery\nЕсть ли у модели отдельный регистр супер-батареи\n(самое глубокое ограничение мощности).\nВлияет на профиль Super Battery." };
        m["mdl_search"]     = new[] { "Search model or firmware…", "Szukaj modelu lub firmware…", "Modell oder Firmware suchen…", "Rechercher un modèle ou firmware…", "Buscar modelo o firmware…", "搜索机型或固件…", "Pesquisar modelo ou firmware…", "Поиск модели или прошивки…" };
        m["mdl_legend"]     = new[] {
            "Fan curve:  ✓ editable = writable now   ·   ◉ unverified = editable after enabling Experimental, but addresses unconfirmed - compare with MSI Center first   ·   — = none.   SB = has a super-battery register.   RPM = fan-tachometer address known (real RPM shown).",
            "Krzywa:  ✓ edytowalna = zapisywalna od razu   ·   ◉ niezweryfikowany = edytowalna po włączeniu trybu eksperymentalnego, ale adresy niepotwierdzone - najpierw porównaj z MSI Center   ·   — = brak.   SB = ma rejestr super-baterii.   RPM = znany adres tachometru (pokazuje realne RPM).",
            "Lüfterkurve:  ✓ editierbar = sofort beschreibbar   ·   ◉ unbestätigt = editierbar nach Aktivieren von Experimentell, Adressen unbestätigt - zuerst mit MSI Center vergleichen   ·   — = keine.   SB = hat ein Super-Battery-Register.   RPM = Tachometer-Adresse bekannt (echte RPM).",
            "Courbe :  ✓ modifiable = inscriptible maintenant   ·   ◉ non vérifiée = modifiable après activation du mode expérimental, adresses non confirmées - comparez d'abord avec MSI Center   ·   — = aucune.   SB = registre super-batterie.   RPM = adresse du tachymètre connue (RPM réels).",
            "Curva:  ✓ editable = escribible ya   ·   ◉ sin verificar = editable tras activar Experimental, direcciones sin confirmar - compara antes con MSI Center   ·   — = ninguna.   SB = registro de super-batería.   RPM = dirección del tacómetro conocida (RPM reales).",
            "风扇曲线：✓ 可编辑 = 立即可写   ·   ◉ 未验证 = 启用实验模式后可编辑，但地址未确认 - 请先与 MSI Center 对比   ·   — = 无。SB = 有超级省电寄存器。RPM = 已知转速地址（显示真实转速）。",
            "Curva:  ✓ editável = gravável agora   ·   ◉ não verificada = editável após ativar Experimental, endereços não confirmados - compare antes com o MSI Center   ·   — = nenhuma.   SB = registro de super-bateria.   RPM = endereço do tacômetro conhecido (RPM reais).",
            "Кривая:  ✓ редактируемая = запись доступна   ·   ◉ не проверена = доступна после включения экспериментального режима, адреса не подтверждены - сначала сравните с MSI Center   ·   — = нет.   SB = есть регистр супер-батареи.   RPM = известен адрес тахометра (реальные обороты)." };
        m["set_grp_look"]   = new[] { "Appearance", "Wygląd", "Darstellung", "Apparence", "Apariencia", "外观", "Aparência", "Вид" };
        m["set_grp_power"]  = new[] { "Power", "Zasilanie", "Energie", "Alimentation", "Energía", "电源", "Energia", "Питание" };
        m["set_grp_start"]  = new[] { "Startup & tray", "Start i zasobnik", "Start & Infobereich", "Démarrage", "Inicio", "启动与托盘", "Inicialização", "Запуск" };
        m["set_grp_updates"]= new[] { "Updates", "Aktualizacje", "Updates", "Mises à jour", "Actualizaciones", "更新", "Atualizações", "Обновления" };
        m["set_grp_tray"]   = new[] { "Tray menu", "Menu w zasobniku", "Infobereichsmenü", "Menu de la barre d'état", "Menú de bandeja", "托盘菜单", "Menu da bandeja", "Меню в трее" };
        // (#23) tray-icon mouse actions
        m["set_tray_left"]  = new[] { "Left click", "Lewy przycisk", "Linksklick", "Clic gauche", "Clic izquierdo", "左键单击", "Clique esquerdo", "Левый клик" };
        m["set_tray_mid"]   = new[] { "Middle click", "Środkowy przycisk", "Mittelklick", "Clic du milieu", "Clic central", "中键单击", "Clique do meio", "Средний клик" };
        m["set_tray_wheel"] = new[] { "Scroll wheel", "Kółko myszy", "Mausrad", "Molette", "Rueda del ratón", "滚轮", "Roda do mouse", "Колесо мыши" };
        m["act_none"]       = new[] { "Do nothing", "Nic nie rób", "Nichts tun", "Ne rien faire", "No hacer nada", "不执行操作", "Não fazer nada", "Ничего не делать" };
        m["act_show_state"] = new[] { "Show current state", "Pokaż bieżący stan", "Aktuellen Status anzeigen", "Afficher l'état actuel", "Mostrar estado actual", "显示当前状态", "Mostrar estado atual", "Показать текущее состояние" };
        m["act_open"]       = new[] { "Open: {0}", "Otwórz: {0}", "Öffnen: {0}", "Ouvrir : {0}", "Abrir: {0}", "打开：{0}", "Abrir: {0}", "Открыть: {0}" };
        m["twa_profiles"]   = new[] { "Switch profiles", "Przełączaj profile", "Profile wechseln", "Changer de profil", "Cambiar perfiles", "切换情景模式", "Alternar perfis", "Переключение профилей" };
        // (#26) keyboard backlight
        m["kbd_title"]      = new[] { "Keyboard backlight", "Podświetlenie klawiatury", "Tastaturbeleuchtung", "Rétroéclairage du clavier", "Retroiluminación del teclado", "键盘背光", "Retroiluminação do teclado", "Подсветка клавиатуры" };
        m["kbd_off"]        = new[] { "Off", "Wył.", "Aus", "Éteint", "Apagado", "关闭", "Desligado", "Выкл." };
        m["kbd_low"]        = new[] { "Low", "Niska", "Niedrig", "Faible", "Baja", "低", "Baixa", "Низкая" };
    }

    private static void L09(Dictionary<string, string[]> m)
    {
        m["kbd_mid"]        = new[] { "Mid", "Średnia", "Mittel", "Moyen", "Media", "中", "Média", "Средняя" };
        m["kbd_high"]       = new[] { "High", "Wysoka", "Hoch", "Élevé", "Alta", "高", "Alta", "Высокая" };
        // signed model database (ModelDb)
        m["temptray_grp"]     = new[] { "Temperature in the tray", "Temperatura w zasobniku", "Temperatur im Infobereich", "Température dans la zone de notification", "Temperatura en la bandeja", "托盘温度显示", "Temperatura na bandeja", "Температура в трее" };
        m["temptray_warn"]    = new[] { "Warm above", "Ciepło powyżej", "Warm ab", "Tiède au-dessus de", "Cálido por encima de", "偏热阈值", "Morno acima de", "Тепло выше" };
        m["temptray_hot"]     = new[] { "Hot above", "Gorąco powyżej", "Heiß ab", "Chaud au-dessus de", "Caliente por encima de", "高温阈值", "Quente acima de", "Горячо выше" };
        m["temptray_ok"]      = new[] { "Normal", "Normalna", "Normal", "Normale", "Normal", "正常", "Normal", "Норма" };
        m["temptray_warn_c"]  = new[] { "Warm", "Ciepła", "Warm", "Tiède", "Cálido", "偏热", "Morno", "Тепло" };
        m["temptray_hot_c"]   = new[] { "Hot", "Gorąca", "Heiß", "Chaud", "Caliente", "高温", "Quente", "Горячо" };
        m["temptray_reset"]   = new[] { "Default colours", "Domyślne kolory", "Standardfarben", "Couleurs par défaut", "Colores predeterminados", "默认颜色", "Cores padrão", "Цвета по умолчанию" };
        m["temptray_desc"]    = new[]
        {
            "Shows the temperature as a number next to the clock, without opening anything. Two separate icons, because a tray icon only fits two digits. Windows hides new icons in the overflow area at first - drag them onto the taskbar to keep them visible.",
            "Pokazuje temperaturę jako liczbę przy zegarku, bez otwierania czegokolwiek. Dwie osobne ikony, bo w ikonie zasobnika mieszczą się tylko dwie cyfry. Windows domyślnie chowa nowe ikony w schowku przepełnienia - przeciągnij je na pasek zadań, żeby były widoczne.",
            "Zeigt die Temperatur als Zahl neben der Uhr, ohne etwas zu öffnen. Zwei getrennte Symbole, denn in ein Infobereich-Symbol passen nur zwei Ziffern. Windows versteckt neue Symbole zunächst im Überlaufbereich - zieh sie auf die Taskleiste.",
            "Affiche la température sous forme de nombre près de l'horloge, sans rien ouvrir. Deux icônes distinctes, car une icône ne contient que deux chiffres. Windows masque d'abord les nouvelles icônes dans la zone de débordement - faites-les glisser sur la barre des tâches.",
            "Muestra la temperatura como un número junto al reloj, sin abrir nada. Dos iconos separados, porque en un icono de la bandeja solo caben dos dígitos. Windows oculta al principio los iconos nuevos en el área de desbordamiento: arrástralos a la barra de tareas.",
            "在时钟旁以数字显示温度，无需打开任何窗口。使用两个独立图标，因为托盘图标只能容纳两位数字。Windows 默认会把新图标收进溢出区域，请把它们拖到任务栏上。",
            "Mostra a temperatura como um número ao lado do relógio, sem abrir nada. Dois ícones separados, porque um ícone da bandeja só comporta dois dígitos. O Windows esconde ícones novos na área de transbordo - arraste-os para a barra de tarefas.",
            "Показывает температуру числом рядом с часами, ничего не открывая. Два отдельных значка, потому что в значок трея помещаются лишь две цифры. Windows сначала прячет новые значки в области переполнения - перетащите их на панель задач."
        };
        m["set_grp_nav"]      = new[] { "Navigation", "Nawigacja", "Navigation", "Navigation", "Navegación", "导航", "Navegação", "Навигация" };
        m["set_always_start"] = new[] { "Always open on Start", "Zawsze otwieraj na Start", "Immer auf Start öffnen", "Toujours ouvrir sur Accueil", "Abrir siempre en Inicio", "总是从起始页打开", "Abrir sempre no Início", "Всегда открывать на Старте" };
        m["set_always_start_desc"] = new[]
        {
            "Settings open on the Start dashboard every time instead of the sub-tab you used last. Clicking the Settings tab while you are already on it always goes back to Start.",
            "Ustawienia otwierają się zawsze na stronie Start, zamiast wracać do ostatnio używanej podzakładki. Ponowne kliknięcie zakładki Ustawienia, gdy już w nich jesteś, zawsze wraca na Start.",
            "Die Einstellungen öffnen immer das Start-Dashboard statt des zuletzt benutzten Unterreiters. Ein erneuter Klick auf den Reiter Einstellungen führt immer zurück zu Start.",
            "Les paramètres s'ouvrent toujours sur l'accueil au lieu du sous-onglet utilisé en dernier. Recliquer sur l'onglet Paramètres y ramène toujours.",
            "Los ajustes se abren siempre en la página de inicio en lugar de la subpestaña que usaste por última vez. Volver a pulsar la pestaña Ajustes siempre regresa a Inicio.",
            "设置每次都从起始页打开，而不是上次使用的子选项卡。已经在设置中时再次点击该选项卡也会回到起始页。",
            "As configurações abrem sempre na página inicial em vez da subguia usada por último. Clicar novamente na guia Configurações sempre volta ao Início.",
            "Настройки всегда открываются на стартовой странице вместо последней использованной вкладки. Повторный щелчок по вкладке настроек всегда возвращает на старт."
        };
        m["set_modeldb"]    = new[] { "Model database", "Baza modeli", "Modell-Datenbank", "Base de modèles", "Base de modelos", "型号数据库", "Base de modelos", "База моделей" };
        m["modeldb_downloaded"] = new[] { "updated", "zaktualizowana", "aktualisiert", "mise à jour", "actualizada", "已更新", "atualizada", "обновлена" };
        m["modeldb_pending"] = new[] { "{0} waiting", "{0} czeka", "{0} wartet", "{0} en attente", "{0} en espera", "{0} 等待中", "{0} aguardando", "{0} ожидает" };
        m["log_modeldb"]    = new[] { "Model database updated to {0}", "Baza modeli zaktualizowana do {0}", "Modell-Datenbank auf {0} aktualisiert", "Base de modèles mise à jour vers {0}", "Base de modelos actualizada a {0}", "型号数据库已更新到 {0}", "Base de modelos atualizada para {0}", "База моделей обновлена до {0}" };
        m["modeldb_check"] = new[] { "Check now", "Sprawdź teraz", "Jetzt prüfen", "Vérifier", "Comprobar ahora", "立即检查", "Verificar agora", "Проверить" };
        m["modeldb_checking"] = new[] { "Checking…", "Sprawdzam…", "Prüfe…", "Vérification…", "Comprobando…", "检查中…", "Verificando…", "Проверка…" };
        m["modeldb_current"] = new[] { "Already up to date", "Już aktualna", "Bereits aktuell", "Déjà à jour", "Ya actualizada", "已是最新", "Já atualizada", "Уже актуальна" };
        m["modeldb_applied"] = new[] { "Updated to {0}", "Zaktualizowano do {0}", "Auf {0} aktualisiert", "Mis à jour vers {0}", "Actualizada a {0}", "已更新到 {0}", "Atualizada para {0}", "Обновлена до {0}" };
        m["modeldb_failed"] = new[] { "Could not check", "Nie udało się sprawdzić", "Prüfung fehlgeschlagen", "Vérification impossible", "No se pudo comprobar", "检查失败", "Não foi possível verificar", "Не удалось проверить" };
        m["modeldb_deferred"] = new[] { "Downloaded - applies when the fan-curve editor is closed", "Pobrano - zadziała po zamknięciu edytora krzywej", "Geladen - wirkt, sobald der Lüfterkurven-Editor geschlossen ist", "Téléchargée - active à la fermeture de l'éditeur de courbe", "Descargada - se aplica al cerrar el editor de curva", "已下载 - 关闭风扇曲线编辑器后生效", "Baixada - aplica ao fechar o editor de curva", "Загружена - применится после закрытия редактора кривой" };
        // Windows-key lock (software hook)
        m["winlock_title"]  = new[] { "Windows key lock", "Blokada klawisza Windows", "Windows-Taste sperren", "Verrou touche Windows", "Bloqueo tecla Windows", "Windows 键锁定", "Bloqueio da tecla Windows", "Блокировка клавиши Windows" };
        m["winlock_hint"]   = new[]
        {
            "Blocks both Windows keys while gaming (Win+L too; Ctrl+Alt+Del still works)",
            "Blokuje oba klawisze Windows podczas gry (Win+L też; Ctrl+Alt+Del działa)",
            "Sperrt beide Windows-Tasten beim Spielen (auch Win+L; Strg+Alt+Entf geht weiter)",
            "Bloque les deux touches Windows en jeu (Win+L aussi ; Ctrl+Alt+Suppr fonctionne toujours)",
            "Bloquea ambas teclas Windows al jugar (Win+L también; Ctrl+Alt+Supr sigue funcionando)",
            "游戏时屏蔽两个 Windows 键（包括 Win+L；Ctrl+Alt+Del 仍可用）",
            "Bloqueia as duas teclas Windows ao jogar (Win+L também; Ctrl+Alt+Del continua funcionando)",
            "Блокирует обе клавиши Windows в игре (включая Win+L; Ctrl+Alt+Del работает)",
        };
        // screen brightness (scenes + CLI)
        m["bri_title"]      = new[] { "Screen brightness", "Jasność ekranu", "Bildschirmhelligkeit", "Luminosité de l'écran", "Brillo de pantalla", "屏幕亮度", "Brilho da tela", "Яркость экрана" };
        // scene schedule
        m["sch_grp"]        = new[] { "Scene schedule", "Harmonogram scen", "Szenen-Zeitplan", "Planification des scènes", "Programación de escenas", "场景计划", "Agendamento de cenas", "Расписание сцен" };
        m["sch_enable"]     = new[] { "Schedule active", "Harmonogram aktywny", "Zeitplan aktiv", "Planification active", "Programación activa", "计划已启用", "Agendamento ativo", "Расписание включено" };
        m["sch_desc"]       = new[]
        {
            "Applies a scene when its time window starts (first matching rule wins; overnight ranges allowed; also applied at startup). Manual changes inside a window are respected.",
            "Uruchamia scenę na początku jej okna czasowego (wygrywa pierwsza pasująca reguła; zakresy przez północ dozwolone; działa też przy starcie aplikacji). Ręczne zmiany w trakcie okna są respektowane.",
            "Wendet eine Szene an, wenn ihr Zeitfenster beginnt (erste passende Regel gewinnt; Bereiche über Mitternacht erlaubt; auch beim Start). Manuelle Änderungen im Fenster bleiben erhalten.",
            "Applique une scène au début de sa plage horaire (la première règle correspondante gagne ; plages passant minuit autorisées ; aussi au démarrage). Les changements manuels dans la plage sont respectés.",
            "Aplica una escena cuando comienza su franja horaria (gana la primera regla que coincida; se permiten rangos que cruzan la medianoche; también al iniciar). Los cambios manuales dentro de la franja se respetan.",
            "在时间窗口开始时应用场景（第一条匹配规则生效；允许跨午夜的区间；启动时也会应用）。窗口内的手动更改会被保留。",
            "Aplica uma cena quando sua janela de tempo começa (a primeira regra correspondente vence; intervalos que cruzam a meia-noite são permitidos; também na inicialização). Alterações manuais dentro da janela são respeitadas.",
            "Применяет сцену в начале её временного окна (побеждает первое совпавшее правило; диапазоны через полночь допустимы; также при запуске). Ручные изменения внутри окна сохраняются.",
        };
        m["sch_add"]        = new[] { "Add rule", "Dodaj regułę", "Regel hinzufügen", "Ajouter une règle", "Añadir regla", "添加规则", "Adicionar regra", "Добавить правило" };
        m["sch_need_scene"] = new[]
        {
            "Create a scene first - schedule rules run scenes.",
            "Najpierw utwórz scenę - reguły harmonogramu uruchamiają sceny.",
            "Erst eine Szene anlegen - Zeitplanregeln starten Szenen.",
            "Créez d'abord une scène - les règles lancent des scènes.",
            "Crea primero una escena - las reglas ejecutan escenas.",
            "请先创建场景 - 计划规则运行的是场景。",
            "Crie uma cena primeiro - as regras executam cenas.",
            "Сначала создайте сцену - правила расписания запускают сцены.",
        };
        m["sch_rule_title"] = new[] { "Schedule rule", "Reguła harmonogramu", "Zeitplan-Regel", "Règle de planification", "Regla de programación", "计划规则", "Regra de agendamento", "Правило расписания" };
        m["sch_scene"]      = new[] { "Scene", "Scena", "Szene", "Scène", "Escena", "场景", "Cena", "Сцена" };
        m["sch_days"]       = new[] { "Days", "Dni", "Tage", "Jours", "Días", "星期", "Dias", "Дни" };
        m["sch_from"]       = new[] { "From", "Od", "Von", "De", "Desde", "从", "De", "С" };
        m["sch_to"]         = new[] { "to", "do", "bis", "à", "hasta", "到", "até", "до" };
        m["sch_daily"]      = new[] { "Every day", "Codziennie", "Täglich", "Tous les jours", "Todos los días", "每天", "Todos os dias", "Ежедневно" };
        m["sch_weekdays"]   = new[] { "Weekdays", "Dni robocze", "Werktags", "Jours ouvrés", "Días laborables", "工作日", "Dias úteis", "Будни" };
        m["sch_weekend"]    = new[] { "Weekend", "Weekend", "Wochenende", "Week-end", "Fin de semana", "周末", "Fim de semana", "Выходные" };
        m["scen_gear_tip"]  = new[] { "Choose what this tab shows", "Wybierz, co pokazuje ta zakładka", "Wähle, was dieser Tab zeigt", "Choisissez ce que cet onglet affiche", "Elige qué muestra esta pestaña", "选择此选项卡显示的内容", "Escolha o que esta guia mostra", "Выберите, что показывает эта вкладка" };
        m["log_schedule"]   = new[] { "Schedule: {0} ({1}-{2})", "Harmonogram: {0} ({1}-{2})", "Zeitplan: {0} ({1}-{2})", "Planification : {0} ({1}-{2})", "Programación: {0} ({1}-{2})", "计划：{0}（{1}-{2}）", "Agendamento: {0} ({1}-{2})", "Расписание: {0} ({1}-{2})" };
        m["log_src_schedule"] = new[] { "schedule", "harmonogram", "Zeitplan", "planification", "programación", "计划", "agendamento", "расписание" };
        // touchpad (devnode switch)
        m["tp_title"]       = new[] { "Touchpad", "Touchpad", "Touchpad", "Pavé tactile", "Panel táctil", "触摸板", "Touchpad", "Тачпад" };
        m["tp_hint"]        = new[]
        {
            "Disables the touchpad at the device level (like Device Manager). The hotkey and a panic reset always re-enable it",
            "Wyłącza touchpad na poziomie urządzenia (jak Menedżer urządzeń). Hotkey i reset awaryjny zawsze go włączą z powrotem",
            "Deaktiviert das Touchpad auf Geräteebene (wie der Geräte-Manager). Hotkey und Not-Reset aktivieren es immer wieder",
            "Désactive le pavé tactile au niveau du périphérique (comme le Gestionnaire de périphériques). Le raccourci et la réinitialisation d'urgence le réactivent toujours",
            "Desactiva el panel táctil a nivel de dispositivo (como el Administrador de dispositivos). El atajo y el reinicio de emergencia siempre lo reactivan",
            "在设备级别禁用触摸板（如设备管理器）。热键和紧急重置始终可以重新启用它",
            "Desativa o touchpad no nível do dispositivo (como o Gerenciador de Dispositivos). O atalho e o reset de emergência sempre o reativam",
            "Отключает тачпад на уровне устройства (как Диспетчер устройств). Горячая клавиша и аварийный сброс всегда включают его обратно",
        };
        // battery-level rules
        m["bat_rules_grp"]  = new[] { "Battery rules", "Reguły baterii", "Akku-Regeln", "Règles de batterie", "Reglas de batería", "电量规则", "Regras de bateria", "Правила батареи" };
        m["bat_rules_desc"] = new[]
        {
            "Fires once when the battery level crosses a threshold: the lower rule while discharging, the upper one while charging. A rule re-arms only after the level moves 3 pp away from its threshold, so a 30 ↔ 31 % wobble never switches back and forth.",
            "Odpala raz, gdy poziom baterii przecina próg: dolna reguła przy rozładowywaniu, górna przy ładowaniu. Reguła uzbraja się ponownie dopiero, gdy bateria odjedzie 3 p.p. od progu, więc wahnięcie 30 ↔ 31 % nie powoduje przełączania w kółko.",
            "Löst einmal aus, wenn der Akkustand eine Schwelle kreuzt: die untere Regel beim Entladen, die obere beim Laden. Eine Regel wird erst wieder scharf, wenn der Stand 3 Pp. von der Schwelle wegwandert - ein Pendeln um 30 ↔ 31 % schaltet also nie hin und her.",
            "Se déclenche une fois quand le niveau franchit un seuil : règle basse en décharge, règle haute en charge. Une règle ne se réarme que lorsque le niveau s'éloigne de 3 pts du seuil - une oscillation 30 ↔ 31 % ne provoque donc jamais d'allers-retours.",
            "Se activa una vez cuando el nivel cruza un umbral: la regla inferior al descargar, la superior al cargar. Una regla solo se rearma cuando el nivel se aleja 3 pp del umbral, así que una oscilación de 30 ↔ 31 % nunca cambia de un lado a otro.",
            "电量越过阈值时触发一次：放电时触发下限规则，充电时触发上限规则。规则只有在电量离开阈值 3 个百分点后才会重新武装，因此 30 ↔ 31% 的波动不会来回切换。",
            "Dispara uma vez quando o nível cruza um limite: a regra inferior ao descarregar, a superior ao carregar. Uma regra só rearma quando o nível se afasta 3 pp do limite, então uma oscilação de 30 ↔ 31 % nunca fica alternando.",
            "Срабатывает один раз при пересечении порога: нижнее правило при разряде, верхнее при зарядке. Правило взводится снова лишь когда уровень отойдёт на 3 п.п. от порога, поэтому колебание 30 ↔ 31 % не вызывает переключений туда-сюда.",
        };
        m["bat_enable"]     = new[] { "Rules active", "Reguły aktywne", "Regeln aktiv", "Règles actives", "Reglas activas", "规则已启用", "Regras ativas", "Правила включены" };
        m["bat_below"]      = new[] { "Below", "Poniżej", "Unter", "Sous", "Por debajo", "低于", "Abaixo", "Ниже" };
        m["bat_above"]      = new[] { "Above", "Powyżej", "Über", "Au-dessus", "Por encima", "高于", "Acima", "Выше" };
        m["log_batt_low"]   = new[] { "Battery {0} % - crossed below {1} %", "Bateria {0} % - spadła poniżej {1} %", "Akku {0} % - unter {1} % gefallen", "Batterie {0} % - passée sous {1} %", "Batería {0} % - cayó por debajo de {1} %", "电量 {0}% - 低于 {1}%", "Bateria {0} % - caiu abaixo de {1} %", "Батарея {0} % - опустилась ниже {1} %" };
        m["log_batt_high"]  = new[] { "Battery {0} % - crossed above {1} %", "Bateria {0} % - wzrosła powyżej {1} %", "Akku {0} % - über {1} % gestiegen", "Batterie {0} % - passée au-dessus de {1} %", "Batería {0} % - superó el {1} %", "电量 {0}% - 高于 {1}%", "Bateria {0} % - subiu acima de {1} %", "Батарея {0} % - поднялась выше {1} %" };
        m["log_src_battery"] = new[] { "battery", "bateria", "Akku", "batterie", "batería", "电量", "bateria", "батарея" };
        // Fn/Win swap (EC fn_win_swap)
        m["fnswap_grp"]     = new[] { "Keyboard layout", "Układ klawiatury", "Tastaturlayout", "Disposition du clavier", "Distribución del teclado", "键盘布局", "Layout do teclado", "Раскладка клавиатуры" };
        m["fnswap_title"]   = new[] { "Fn / Win keys", "Klawisze Fn / Win", "Fn/Win-Tasten", "Touches Fn / Win", "Teclas Fn / Win", "Fn / Win 键", "Teclas Fn / Win", "Клавиши Fn / Win" };
        m["fnswap_left"]    = new[] { "Fn on the left", "Fn po lewej", "Fn links", "Fn à gauche", "Fn a la izquierda", "Fn 在左", "Fn à esquerda", "Fn слева" };
        m["fnswap_right"]   = new[] { "Fn on the right", "Fn po prawej", "Fn rechts", "Fn à droite", "Fn a la derecha", "Fn 在右", "Fn à direita", "Fn справа" };
        m["fnswap_desc"]    = new[]
        {
            "Swaps the Fn and Windows keys in hardware (the setting lives in the EC and survives reboots). Pick the side the Fn key should be on.",
            "Zamienia klawisze Fn i Windows sprzętowo (ustawienie mieszka w EC i przetrwa restart). Wybierz, po której stronie ma być klawisz Fn.",
            "Tauscht die Fn- und Windows-Taste in Hardware (die Einstellung liegt im EC und übersteht Neustarts). Wähle, auf welcher Seite die Fn-Taste liegen soll.",
            "Échange les touches Fn et Windows au niveau matériel (le réglage vit dans l'EC et survit aux redémarrages). Choisissez de quel côté doit être la touche Fn.",
            "Intercambia las teclas Fn y Windows por hardware (el ajuste vive en el EC y sobrevive a los reinicios). Elige en qué lado debe estar la tecla Fn.",
            "在硬件层面交换 Fn 和 Windows 键（设置保存在 EC 中，重启后仍然有效）。选择 Fn 键应位于哪一侧。",
            "Troca as teclas Fn e Windows em hardware (a configuração fica no EC e sobrevive a reinicializações). Escolha de que lado deve ficar a tecla Fn.",
            "Меняет местами клавиши Fn и Windows на аппаратном уровне (настройка хранится в EC и переживает перезагрузку). Выберите, с какой стороны должна быть клавиша Fn.",
        };
        // (#27) webcam switch + hard block
        m["webcam_title"]   = new[] { "Webcam", "Kamera", "Webcam", "Webcam", "Cámara web", "摄像头", "Webcam", "Веб-камера" };
        m["webcam_hint"]    = new[] { "Hardware switch: the camera drops off USB (same as the Fn key)", "Sprzętowy przełącznik: kamera znika z USB (jak klawisz Fn)", "Hardware-Schalter: Kamera verschwindet vom USB (wie die Fn-Taste)", "Interrupteur matériel : la caméra disparaît de l'USB (comme la touche Fn)", "Interruptor de hardware: la cámara desaparece del USB (como la tecla Fn)", "硬件开关：摄像头从 USB 断开（与 Fn 键相同）", "Interruptor de hardware: a câmera some do USB (como a tecla Fn)", "Аппаратный переключатель: камера исчезает с USB (как клавиша Fn)" };
        m["set_grp_privacy"]= new[] { "Privacy", "Prywatność", "Datenschutz", "Confidentialité", "Privacidad", "隐私", "Privacidade", "Приватность" };
        m["webcam_block"]   = new[] { "Hard camera block", "Twarda blokada kamery", "Harte Kamerasperre", "Blocage matériel de la caméra", "Bloqueo duro de la cámara", "硬件级摄像头锁定", "Bloqueio rígido da câmera", "Жёсткая блокировка камеры" };
        m["webcam_block_desc"] = new[]
        {
            "Locks the camera off at the firmware level: while active, neither the Fn key nor the Webcam switch can re-enable it. Lift the block here; a panic reset also lifts it.",
            "Blokuje kamerę na poziomie firmware: dopóki aktywna, ani klawisz Fn, ani przełącznik Kamera jej nie włączą. Blokadę zdejmiesz tutaj; reset awaryjny też ją zdejmuje.",
            "Sperrt die Kamera auf Firmware-Ebene: solange aktiv, kann weder die Fn-Taste noch der Webcam-Schalter sie aktivieren. Die Sperre wird hier aufgehoben; ein Not-Reset hebt sie ebenfalls auf.",
            "Verrouille la caméra au niveau du firmware : tant qu'il est actif, ni la touche Fn ni l'interrupteur Webcam ne peuvent la réactiver. Levez le blocage ici ; une réinitialisation d'urgence le lève aussi.",
            "Bloquea la cámara a nivel de firmware: mientras esté activo, ni la tecla Fn ni el interruptor de la cámara pueden reactivarla. Quita el bloqueo aquí; un reinicio de emergencia también lo quita.",
            "在固件层面锁定摄像头：激活期间，Fn 键和摄像头开关都无法重新启用它。可在此解除锁定；紧急重置也会解除。",
            "Bloqueia a câmera no nível do firmware: enquanto ativo, nem a tecla Fn nem o interruptor da Webcam podem reativá-la. Remova o bloqueio aqui; um reset de emergência também o remove.",
            "Блокирует камеру на уровне прошивки: пока блокировка активна, ни клавиша Fn, ни переключатель камеры не смогут её включить. Снять блокировку можно здесь; аварийный сброс тоже её снимает.",
        };
    }

    private static void L10(Dictionary<string, string[]> m)
    {
        m["webcam_blocked"] = new[] { "Block active", "Blokada aktywna", "Sperre aktiv", "Blocage actif", "Bloqueo activo", "锁定已启用", "Bloqueio ativo", "Блокировка активна" };
        m["webcam_unblocked"] = new[] { "Block lifted", "Blokada zdjęta", "Sperre aufgehoben", "Blocage levé", "Bloqueo quitado", "锁定已解除", "Bloqueio removido", "Блокировка снята" };
        m["webcam_blocked_warn"] = new[] { "Camera is hard-blocked - lift the block in Settings → System", "Kamera jest twardo zablokowana - zdejmij blokadę w Ustawienia → System", "Kamera ist hart gesperrt - Sperre unter Einstellungen → System aufheben", "Caméra bloquée matériellement - levez le blocage dans Paramètres → Système", "La cámara está bloqueada - quita el bloqueo en Ajustes → Sistema", "摄像头已被硬件锁定 - 请在设置 → 系统中解除锁定", "A câmera está bloqueada - remova o bloqueio em Configurações → Sistema", "Камера жёстко заблокирована - снимите блокировку в Настройки → Система" };
        // (#21) scenes
        m["scene_title"]    = new[] { "Scenes", "Sceny", "Szenen", "Scènes", "Escenas", "自定义场景", "Cenas", "Сцены" };
        m["scene_applied"]  = new[] { "Scene applied", "Scena zastosowana", "Szene angewendet", "Scène appliquée", "Escena aplicada", "场景已应用", "Cena aplicada", "Сцена применена" };
        m["log_scene"]      = new[] { "Scene: {0}", "Scena: {0}", "Szene: {0}", "Scène : {0}", "Escena: {0}", "场景：{0}", "Cena: {0}", "Сцена: {0}" };
        m["log_src_scene"]  = new[] { "Scene", "Scena", "Szene", "Scène", "Escena", "场景", "Cena", "Сцена" };
        m["scene_add"]      = new[] { "Add scene", "Dodaj scenę", "Szene hinzufügen", "Ajouter une scène", "Añadir escena", "添加场景", "Adicionar cena", "Добавить сцену" };
        m["scene_add_examples"] = new[] { "Add example scenes", "Dodaj przykładowe sceny", "Beispielszenen hinzufügen", "Ajouter des scènes d'exemple", "Añadir escenas de ejemplo", "添加示例场景", "Adicionar cenas de exemplo", "Добавить примеры сцен" };
        m["scene_run"]      = new[] { "Run", "Uruchom", "Ausführen", "Exécuter", "Ejecutar", "运行", "Executar", "Запустить" };
        m["scene_edit"]     = new[] { "Edit", "Edytuj", "Bearbeiten", "Modifier", "Editar", "编辑", "Editar", "Изменить" };
        m["scene_delete"]   = new[] { "Delete", "Usuń", "Löschen", "Supprimer", "Eliminar", "删除", "Excluir", "Удалить" };
        m["scene_up"]       = new[] { "Move up", "Przesuń w górę", "Nach oben", "Monter", "Subir", "上移", "Mover para cima", "Вверх" };
        m["scene_down"]     = new[] { "Move down", "Przesuń w dół", "Nach unten", "Descendre", "Bajar", "下移", "Mover para baixo", "Вниз" };
        m["scene_del_confirm"] = new[] { "Delete scene \"{0}\"?", "Usunąć scenę \"{0}\"?", "Szene \"{0}\" löschen?", "Supprimer la scène « {0} » ?", "¿Eliminar la escena \"{0}\"?", "删除场景“{0}”？", "Excluir a cena \"{0}\"?", "Удалить сцену \"{0}\"?" };
        m["scene_empty"]    = new[]
        {
            "No scenes yet. A scene applies profile, fan curve, refresh rate, overlay and more in one click.",
            "Brak scen. Scena jednym kliknięciem ustawia profil, krzywą, odświeżanie, overlay i więcej.",
            "Noch keine Szenen. Eine Szene setzt Profil, Lüfterkurve, Bildwiederholrate, Overlay und mehr mit einem Klick.",
            "Aucune scène. Une scène applique profil, courbe, taux de rafraîchissement, overlay et plus en un clic.",
            "Sin escenas. Una escena aplica perfil, curva, tasa de refresco, overlay y más con un clic.",
            "尚无场景。一个场景可一键设置情景模式、风扇曲线、刷新率、悬浮窗等。",
            "Nenhuma cena ainda. Uma cena aplica perfil, curva, taxa de atualização, overlay e mais em um clique.",
            "Сцен пока нет. Сцена одним кликом применяет профиль, кривую, частоту обновления, оверлей и другое.",
        };
        m["scene_empty_def"] = new[] { "(no changes)", "(bez zmian)", "(keine Änderungen)", "(aucun changement)", "(sin cambios)", "（无更改）", "(sem alterações)", "(без изменений)" };
        m["scene_name"]     = new[] { "Name", "Nazwa", "Name", "Nom", "Nombre", "名称", "Nome", "Название" };
        m["scene_glyph"]    = new[] { "Icon (optional)", "Ikona (opcjonalnie)", "Symbol (optional)", "Icône (facultatif)", "Icono (opcional)", "图标（可选）", "Ícone (opcional)", "Значок (необязательно)" };
        m["scene_hint_unchecked"] = new[]
        {
            "Rows switched on are applied when the scene runs; everything else stays as it is.",
            "Włączone wiersze są ustawiane przy uruchomieniu sceny; reszta zostaje bez zmian.",
            "Aktivierte Zeilen werden beim Ausführen der Szene gesetzt; alles andere bleibt unverändert.",
            "Les lignes activées sont appliquées à l'exécution de la scène ; le reste ne change pas.",
            "Las filas activadas se aplican al ejecutar la escena; el resto no cambia.",
            "启用的行会在运行场景时被应用；其余保持不变。",
            "As linhas ativadas são aplicadas ao executar a cena; o resto permanece como está.",
            "Включённые строки применяются при запуске сцены; остальное остаётся без изменений.",
        };
        m["sc_profile"]     = new[] { "Profile", "Profil", "Profil", "Profil", "Perfil", "情景模式", "Perfil", "Профиль" };
        m["scene_example_work"]   = new[] { "Work", "Praca", "Arbeit", "Travail", "Trabajo", "办公", "Trabalho", "Работа" };
        m["scene_example_travel"] = new[] { "Travel", "Podróż", "Reise", "Voyage", "Viaje", "出行", "Viagem", "Поездка" };
        m["twa_scenes"]     = new[] { "Switch scenes", "Przełączaj sceny", "Szenen wechseln", "Changer de scène", "Cambiar escenas", "切换场景", "Alternar cenas", "Переключение сцен" };
        m["twa_kbd"]        = new[] { "Keyboard backlight", "Podświetlenie klawiatury", "Tastaturbeleuchtung", "Rétroéclairage du clavier", "Retroiluminación del teclado", "键盘背光", "Retroiluminação do teclado", "Подсветка клавиатуры" };
        // feedback round 2
        m["set_refresh_now"] = new[] { "Current refresh rate", "Aktualne odświeżanie", "Aktuelle Bildwiederholrate", "Taux de rafraîchissement actuel", "Tasa de refresco actual", "当前刷新率", "Taxa de atualização atual", "Текущая частота обновления" };
        m["ref_panel_internal"] = new[] { "Controls the built-in laptop panel", "Steruje wbudowaną matrycą laptopa", "Steuert das integrierte Laptop-Display", "Contrôle la dalle intégrée du portable", "Controla el panel integrado del portátil", "控制笔记本内置屏幕", "Controla a tela integrada do notebook", "Управляет встроенным экраном ноутбука" };
        m["ref_panel_primary"] = new[] { "No built-in panel active - controls the primary display", "Brak aktywnej wbudowanej matrycy - sterowanie ekranem głównym", "Kein integriertes Display aktiv - steuert das Hauptdisplay", "Aucune dalle intégrée active - contrôle l'écran principal", "Sin panel integrado activo - controla la pantalla principal", "没有活动的内置屏幕 - 控制主屏幕", "Nenhuma tela integrada ativa - controla a tela principal", "Встроенный экран не активен - управление основным экраном" };
        m["webcam_block_confirm"] = new[] { "Confirm the block", "Potwierdź blokadę", "Sperre bestätigen", "Confirmer le blocage", "Confirmar el bloqueo", "确认锁定", "Confirmar o bloqueio", "Подтвердить блокировку" };
        m["scene_example_current"] = new[] { "Current setup", "Bieżące ustawienia", "Aktuelles Setup", "Configuration actuelle", "Configuración actual", "当前设置", "Configuração atual", "Текущие настройки" };
        m["set_grp_scen"]   = new[] { "Scenarios tab", "Zakładka Scenariusze", "Szenarien-Tab", "Onglet Scénarios", "Pestaña Escenarios", "场景选项卡", "Aba Cenários", "Вкладка Сценарии" };
        m["set_refresh_set"] = new[] { "Change now", "Zmień teraz", "Jetzt ändern", "Changer maintenant", "Cambiar ahora", "立即更改", "Alterar agora", "Изменить сейчас" };
        m["scene_del_arm"]  = new[] { "Click again to delete", "Kliknij ponownie, aby usunąć", "Zum Löschen erneut klicken", "Cliquez à nouveau pour supprimer", "Haz clic de nuevo para eliminar", "再次点击以删除", "Clique novamente para excluir", "Нажмите ещё раз, чтобы удалить" };
        m["ec_view_title"]  = new[] { "EC live view", "Podgląd EC na żywo", "EC-Live-Ansicht", "Vue EC en direct", "Vista EC en vivo", "EC 实时视图", "Visualização EC ao vivo", "Просмотр EC в реальном времени" };
        m["ec_view_marker"] = new[] { "Marker", "Marker", "Marker", "Marqueur", "Marcador", "标记", "Marcador", "Маркер" };
        m["ec_view_noise"]  = new[] { "Muted (always changing): {0}", "Wyciszone (zmieniają się ciągle): {0}", "Stummgeschaltet (ändern sich ständig): {0}", "Masqués (changent en continu) : {0}", "Silenciados (cambian sin parar): {0}", "已静音（持续变化）：{0}", "Silenciados (mudam o tempo todo): {0}", "Заглушены (меняются постоянно): {0}" };
        m["ec_view_hint"]   = new[]
        {
            "Read-only live EC dump, refreshed every 1.5 s. Bytes that just changed glow amber and land in the log below. Press an Fn key (backlight, camera, fans) and watch which register reacts; sensor bytes (temps, fan speeds) flicker naturally.",
            "Podgląd EC tylko do odczytu, odświeżany co 1,5 s. Bajty, które właśnie się zmieniły, świecą na bursztynowo i trafiają do dziennika poniżej. Wciśnij klawisz Fn (podświetlenie, kamera, wentylatory) i obserwuj, który rejestr reaguje; bajty czujników (temperatury, obroty) zmieniają się naturalnie.",
            "Schreibgeschützter Live-EC-Dump, alle 1,5 s aktualisiert. Gerade geänderte Bytes leuchten bernsteinfarben und erscheinen im Protokoll unten. Drücken Sie eine Fn-Taste (Beleuchtung, Kamera, Lüfter) und beobachten Sie, welches Register reagiert; Sensor-Bytes (Temperaturen, Drehzahlen) flackern natürlich.",
            "Vue EC en lecture seule, actualisée toutes les 1,5 s. Les octets qui viennent de changer s'illuminent en ambre et apparaissent dans le journal ci-dessous. Appuyez sur une touche Fn (rétroéclairage, caméra, ventilateurs) et observez quel registre réagit ; les octets de capteurs (températures, vitesses) fluctuent naturellement.",
            "Volcado EC en vivo de solo lectura, actualizado cada 1,5 s. Los bytes que acaban de cambiar brillan en ámbar y aparecen en el registro de abajo. Pulsa una tecla Fn (retroiluminación, cámara, ventiladores) y observa qué registro reacciona; los bytes de sensores (temperaturas, velocidades) fluctúan de forma natural.",
            "只读的 EC 实时转储，每 1.5 秒刷新。刚变化的字节以琥珀色高亮并记录在下方日志中。按下 Fn 键（背光、摄像头、风扇），观察哪个寄存器有反应；传感器字节（温度、转速）会自然波动。",
            "Despejo EC ao vivo somente leitura, atualizado a cada 1,5 s. Bytes que acabaram de mudar brilham em âmbar e entram no registro abaixo. Pressione uma tecla Fn (retroiluminação, câmera, ventoinhas) e veja qual registrador reage; bytes de sensores (temperaturas, rotações) oscilam naturalmente.",
            "Живой дамп EC только для чтения, обновляется каждые 1,5 с. Только что изменившиеся байты подсвечиваются янтарным и попадают в журнал ниже. Нажмите клавишу Fn (подсветка, камера, вентиляторы) и посмотрите, какой регистр отреагирует; байты датчиков (температуры, обороты) меняются естественно.",
        };
        m["set_grp_display"]= new[] { "Display", "Ekran", "Anzeige", "Écran", "Pantalla", "显示", "Tela", "Экран" };
        m["set_sub_home"]   = new[] { "Start", "Start", "Start", "Accueil", "Inicio", "主页", "Início", "Главная" };
        m["set_sub_general"]= new[] { "General", "Ogólne", "Allgemein", "Général", "General", "常规", "Geral", "Общие" };
        m["set_sub_gaming"] = new[] { "Gaming", "Gaming", "Gaming", "Gaming", "Gaming", "游戏", "Gaming", "Игры" };
        m["set_sub_hotkeys"]= new[] { "Hotkeys", "Skróty", "Kürzel", "Raccourcis", "Atajos", "快捷键", "Atalhos", "Клавиши" };
        m["set_sub_system"] = new[] { "System", "System", "System", "Système", "Sistema", "系统", "Sistema", "Система" };
        m["set_tile_general"]= new[] { "Theme, language, colors, app icon", "Motyw, język, kolory, ikona aplikacji", "Design, Sprache, Farben, App-Symbol", "Thème, langue, couleurs, icône", "Tema, idioma, colores, icono", "主题、语言、颜色、图标", "Tema, idioma, cores, ícone", "Тема, язык, цвета, значок" };
        m["set_tile_power"] = new[] { "Charge limit, AC / battery, refresh rate", "Limit ładowania, AC / bateria, odświeżanie", "Ladelimit, Netz / Akku, Bildrate", "Limite de charge, secteur / batterie, Hz", "Límite de carga, CA / batería, Hz", "充电限制、电源/电池、刷新率", "Limite de carga, CA / bateria, Hz", "Лимит заряда, сеть / батарея, герцовка" };
        m["set_tile_notif"] = new[] { "Temperature alert, on-screen messages", "Alert temperatury, komunikaty ekranowe", "Temperaturalarm, Bildschirmmeldungen", "Alerte température, messages à l'écran", "Alerta de temperatura, avisos en pantalla", "温度警报、屏幕提示", "Alerta de temperatura, avisos na tela", "Оповещение о температуре, сообщения" };
        m["set_tile_gaming"]= new[] { "Overlay, metrics, game session report", "Overlay, metryki, raport sesji gry", "Overlay, Metriken, Sitzungsbericht", "Overlay, métriques, rapport de session", "Overlay, métricas, informe de sesión", "悬浮窗、指标、会话报告", "Overlay, métricas, relatório de sessão", "Оверлей, метрики, отчёт сессии" };
    }

    private static void L11(Dictionary<string, string[]> m)
    {
        m["set_tile_hotkeys"]= new[] { "Global keyboard shortcuts", "Globalne skróty klawiszowe", "Globale Tastenkürzel", "Raccourcis clavier globaux", "Atajos de teclado globales", "全局快捷键", "Atalhos de teclado globais", "Глобальные горячие клавиши" };
        m["set_tile_system"]= new[] { "Autostart, updates, tray menu, backup", "Autostart, aktualizacje, menu tray, kopia", "Autostart, Updates, Tray-Menü, Backup", "Démarrage auto, mises à jour, sauvegarde", "Inicio automático, actualizaciones, copia", "自启动、更新、托盘菜单、备份", "Início automático, atualizações, backup", "Автозапуск, обновления, резервная копия" };
        m["st2_limit_on"]   = new[] { "Limit {0}%", "Limit {0}%", "Limit {0}%", "Limite {0}%", "Límite {0}%", "限制 {0}%", "Limite {0}%", "Лимит {0}%" };
        m["st2_limit_off"]  = new[] { "Limit off", "Limit wył.", "Limit aus", "Limite désact.", "Límite des.", "限制关", "Limite desl.", "Лимит выкл" };
        m["st2_hz"]         = new[] { "AC {0} Hz / bat. {1} Hz", "AC {0} Hz / bat. {1} Hz", "Netz {0} / Akku {1} Hz", "Secteur {0} / batt. {1} Hz", "CA {0} / bat. {1} Hz", "电源 {0} / 电池 {1} Hz", "CA {0} / bat. {1} Hz", "Сеть {0} / бат. {1} Гц" };
        m["st2_metrics"]    = new[] { "{0} metrics", "{0} metryk", "{0} Metriken", "{0} métriques", "{0} métricas", "{0} 项指标", "{0} métricas", "{0} метрик" };
        m["st2_hotkeys"]    = new[] { "{0} of {1} enabled", "{0} z {1} włączonych", "{0} von {1} aktiv", "{0} sur {1} actifs", "{0} de {1} activos", "已启用 {0}/{1}", "{0} de {1} ativos", "{0} из {1} включено" };
        m["st2_system"]     = new[] { "Autostart {0} · updates {1}", "Autostart {0} · aktualizacje {1}", "Autostart {0} · Updates {1}", "Démarrage {0} · MAJ {1}", "Inicio {0} · actualiz. {1}", "自启动{0} · 更新{1}", "Início {0} · atualiz. {1}", "Автозапуск {0} · обновления {1}" };
        m["st2_whatsnew"]   = new[] { "What's new in v{0}", "Co nowego w v{0}", "Neu in v{0}", "Nouveautés de la v{0}", "Novedades de v{0}", "v{0} 新功能", "Novidades da v{0}", "Что нового в v{0}" };
        m["st2_exp"]        = new[] { "experimental on", "eksperymentalne wł", "Experimentell an", "expérimental act.", "experimental act.", "实验模式开", "experimental lig.", "эксперим. вкл" };
        m["ec_err_unsupported"] = new[] {
            "This laptop's firmware refused the EC request (WMI reported \"unsupported\"). GhostDeck cannot read or control the EC here - please report the model on GitHub with this message so the access path can be checked.",
            "Firmware tego laptopa odrzucił żądanie do EC (WMI zwróciło \"unsupported\"). GhostDeck nie odczyta ani nie ustawi tu EC - zgłoś model na GitHubie razem z tym komunikatem, aby sprawdzić ścieżkę dostępu.",
            "Die Firmware dieses Laptops hat die EC-Anfrage abgelehnt (WMI meldet \"unsupported\"). GhostDeck kann den EC hier nicht lesen oder steuern - bitte melden Sie das Modell mit dieser Meldung auf GitHub.",
            "Le firmware de cet ordinateur a refusé la requête EC (WMI indique « unsupported »). GhostDeck ne peut ni lire ni piloter l'EC ici - signalez le modèle sur GitHub avec ce message.",
            "El firmware de este portátil rechazó la petición al EC (WMI informa \"unsupported\"). GhostDeck no puede leer ni controlar el EC aquí: informa del modelo en GitHub junto con este mensaje.",
            "本机固件拒绝了该 EC 请求（WMI 返回 \"unsupported\"）。GhostDeck 无法在此读取或控制 EC，请携带此消息在 GitHub 上反馈机型。",
            "O firmware deste notebook recusou a solicitação ao EC (o WMI informou \"unsupported\"). O GhostDeck não consegue ler nem controlar o EC aqui - reporte o modelo no GitHub com esta mensagem.",
            "Прошивка этого ноутбука отклонила запрос к EC (WMI вернул \"unsupported\"). GhostDeck не может читать или управлять EC - сообщите о модели на GitHub вместе с этим сообщением." };
        m["ec_err_denied"] = new[] {
            "Access to the MSI WMI interface was denied. Run GhostDeck as administrator.",
            "Odmowa dostępu do interfejsu WMI MSI. Uruchom GhostDeck jako administrator.",
            "Zugriff auf die MSI-WMI-Schnittstelle verweigert. Starten Sie GhostDeck als Administrator.",
            "Accès à l'interface WMI MSI refusé. Lancez GhostDeck en tant qu'administrateur.",
            "Acceso denegado a la interfaz WMI de MSI. Ejecuta GhostDeck como administrador.",
            "拒绝访问 MSI WMI 接口。请以管理员身份运行 GhostDeck。",
            "Acesso negado à interface WMI da MSI. Execute o GhostDeck como administrador.",
            "Доступ к интерфейсу WMI MSI запрещён. Запустите GhostDeck от имени администратора." };
        m["ec_err_missing"] = new[] {
            "MSI's WMI interface (MSI_ACPI) was not found on this machine - it is present on MSI laptops only.",
            "Nie znaleziono interfejsu WMI MSI (MSI_ACPI) na tym komputerze - występuje tylko w laptopach MSI.",
            "Die MSI-WMI-Schnittstelle (MSI_ACPI) wurde nicht gefunden - sie existiert nur auf MSI-Laptops.",
            "L'interface WMI MSI (MSI_ACPI) est introuvable - elle n'existe que sur les portables MSI.",
            "No se encontró la interfaz WMI de MSI (MSI_ACPI): solo existe en portátiles MSI.",
            "未找到 MSI 的 WMI 接口 (MSI_ACPI)，该接口仅存在于 MSI 笔记本上。",
            "A interface WMI da MSI (MSI_ACPI) não foi encontrada - ela existe apenas em notebooks MSI.",
            "Интерфейс WMI MSI (MSI_ACPI) не найден - он есть только в ноутбуках MSI." };
        m["rep_step"]       = new[] { "Step {0} of {1}", "Krok {0} z {1}", "Schritt {0} von {1}", "Étape {0} sur {1}", "Paso {0} de {1}", "第 {0} / {1} 步", "Etapa {0} de {1}", "Шаг {0} из {1}" };
        m["rep_set_scenario"] = new[] {
            "In MSI Center set the scenario to: {0}, then click Capture.",
            "W MSI Center ustaw scenariusz: {0}, następnie kliknij Przechwyć.",
            "Im MSI Center das Szenario auf {0} setzen, dann Erfassen klicken.",
            "Dans MSI Center, réglez le scénario sur : {0}, puis cliquez sur Capturer.",
            "En MSI Center fija el escenario en: {0}, luego pulsa Capturar.",
            "在 MSI Center 中将场景设为：{0}，然后点击采集。",
            "No MSI Center defina o cenário como: {0}, depois clique em Capturar.",
            "В MSI Center установите сценарий: {0}, затем нажмите «Снять»." };
        m["rep_capture"]    = new[] { "Capture", "Przechwyć", "Erfassen", "Capturer", "Capturar", "采集", "Capturar", "Снять" };
        m["rep_capturing"]  = new[] { "Reading EC…", "Odczyt EC…", "EC wird gelesen…", "Lecture de l'EC…", "Leyendo EC…", "正在读取 EC…", "Lendo EC…", "Чтение EC…" };
        m["rep_captured"]   = new[] { "captured", "przechwycono", "erfasst", "capturé", "capturado", "已采集", "capturado", "снято" };
        m["rep_pending"]    = new[] { "pending", "oczekuje", "ausstehend", "en attente", "pendiente", "待采集", "pendente", "ожидание" };
        m["rep_all_done"]   = new[] {
            "All scenarios captured. The report was copied to your clipboard and saved to a file. Click Finish to open the GitHub form — paste the full report (Ctrl+V) into the \"Full EC dump per scenario (optional, very helpful)\" field.",
            "Wszystkie scenariusze przechwycone. Raport skopiowano do schowka i zapisano do pliku. Kliknij Zakończ, aby otworzyć formularz GitHub — wklej pełny raport (Ctrl+V) w pole \"Full EC dump per scenario (optional, very helpful)\".",
            "Alle Szenarien erfasst. Der Bericht wurde in die Zwischenablage kopiert und als Datei gespeichert. Auf Fertig klicken, um das GitHub-Formular zu öffnen — vollständigen Bericht (Strg+V) in das Feld \"Full EC dump per scenario (optional, very helpful)\" einfügen.",
            "Tous les scénarios capturés. Le rapport a été copié dans le presse-papiers et enregistré. Cliquez sur Terminer pour ouvrir le formulaire GitHub — collez le rapport complet (Ctrl+V) dans le champ \"Full EC dump per scenario (optional, very helpful)\".",
            "Todos los escenarios capturados. El informe se copió al portapapeles y se guardó en un archivo. Pulsa Finalizar para abrir el formulario de GitHub — pega el informe completo (Ctrl+V) en el campo \"Full EC dump per scenario (optional, very helpful)\".",
            "已采集所有场景。报告已复制到剪贴板并保存为文件。点击完成以打开 GitHub 表单——将完整报告（Ctrl+V）粘贴到 \"Full EC dump per scenario (optional, very helpful)\" 字段。",
            "Todos os cenários capturados. O relatório foi copiado para a área de transferência e salvo em arquivo. Clique em Concluir para abrir o formulário do GitHub — cole o relatório completo (Ctrl+V) no campo \"Full EC dump per scenario (optional, very helpful)\".",
            "Все сценарии сняты. Отчёт скопирован в буфер обмена и сохранён в файл. Нажмите «Готово», чтобы открыть форму GitHub — вставьте полный отчёт (Ctrl+V) в поле \"Full EC dump per scenario (optional, very helpful)\"." };
        m["rep_finish"]     = new[] { "Finish & open GitHub", "Zakończ i otwórz GitHub", "Fertig & GitHub öffnen", "Terminer & ouvrir GitHub", "Finalizar y abrir GitHub", "完成并打开 GitHub", "Concluir e abrir GitHub", "Готово и открыть GitHub" };
        m["rep_cancel"]     = new[] { "Cancel", "Anuluj", "Abbrechen", "Annuler", "Cancelar", "取消", "Cancelar", "Отмена" };
        m["rep_saved_to"]   = new[] { "Saved to: {0}", "Zapisano do: {0}", "Gespeichert unter: {0}", "Enregistré dans : {0}", "Guardado en: {0}", "已保存到：{0}", "Salvo em: {0}", "Сохранено в: {0}" };
        m["rep_clip_fail"] = new[] { "The report could not be put on the clipboard, another program was holding it. Open the saved file and copy the text from there.", "Nie udało się wstawić raportu do schowka, bo trzymał go inny program. Otwórz zapisany plik i skopiuj tekst stamtąd.", "Der Bericht konnte nicht in die Zwischenablage kopiert werden, ein anderes Programm hat sie belegt. Öffne die gespeicherte Datei und kopiere den Text von dort.", "Le rapport n'a pas pu être copié dans le presse-papiers, un autre programme le bloquait. Ouvrez le fichier enregistré et copiez le texte depuis celui-ci.", "No se pudo copiar el informe al portapapeles, otro programa lo tenía ocupado. Abre el archivo guardado y copia el texto desde ahí.", "报告无法复制到剪贴板，剪贴板正被其他程序占用。请打开已保存的文件，从那里复制文本。", "Não foi possível copiar o relatório para a área de transferência, outro programa estava com ela. Abra o arquivo salvo e copie o texto de lá.", "Отчёт не удалось скопировать в буфер обмена, его занимала другая программа. Откройте сохранённый файл и скопируйте текст оттуда." };
        m["rep_read_fail"]  = new[] {
            "Couldn't read the EC (is the MSI WMI interface available?). Details: {0}",
            "Nie udało się odczytać EC (czy interfejs WMI MSI jest dostępny?). Szczegóły: {0}",
            "EC konnte nicht gelesen werden (ist die MSI-WMI-Schnittstelle verfügbar?). Details: {0}",
            "Impossible de lire l'EC (l'interface WMI MSI est-elle disponible ?). Détails : {0}",
            "No se pudo leer el EC (¿está disponible la interfaz WMI de MSI?). Detalles: {0}",
            "无法读取 EC（MSI WMI 接口是否可用？）。详情：{0}",
            "Não foi possível ler o EC (a interface WMI da MSI está disponível?). Detalhes: {0}",
            "Не удалось прочитать EC (доступен ли интерфейс MSI WMI?). Подробности: {0}" };

        m["yes"]            = new[] { "Yes", "Tak", "Ja", "Oui", "Sí", "是", "Sim", "Да" };
        m["no"]             = new[] { "No", "Nie", "Nein", "Non", "No", "否", "Não", "Нет" };
        m["err"]            = new[] { "ERROR", "BŁĄD", "FEHLER", "ERREUR", "ERROR", "错误", "ERRO", "ОШИБКА" };

        m["sub_silent"]       = new[] { "quiet · ~30–40 W", "cicho · ~30–40 W", "leise · ~30–40 W", "silencieux · ~30–40 W", "silencioso · ~30–40 W", "安静 · ~30–40 W", "silencioso · ~30–40 W", "тихо · ~30–40 W" };
        m["sub_balanced"]     = new[] { "full power", "pełna moc", "volle Leistung", "pleine puissance", "máxima potencia", "全功率", "potência total", "полная мощность" };
        m["sub_extreme"]      = new[] { "max · loud", "maks · głośno", "max · laut", "max · bruyant", "máx · ruidoso", "最大 · 吵", "máx · ruidoso", "макс · громко" };
        m["sub_superbattery"] = new[] { "saving · ~15 W", "oszczędzanie · ~15 W", "sparen · ~15 W", "économie · ~15 W", "ahorro · ~15 W", "省电 · ~15 W", "economia · ~15 W", "экономия · ~15 W" };
    }

    // ---- power test (Core/PowerTest.cs + the third Report sub-tab) ----
    private static void L12(Dictionary<string, string[]> m)
    {
        m["subtab_power"]      = new[] { "Power test", "Test mocy", "Leistungstest", "Test de puissance", "Prueba de potencia", "功率测试", "Teste de potência", "Тест мощности" };
        m["pt_intro"]         = new[] { "This sub-tab is the only one that measures rather than just reads. It runs the same load on the processor and the graphics chip in SILENT, BALANCED and EXTREME, recording temperatures, fan speed, CPU clock and how much work the processor actually got done, once a second. That way a report carries numbers instead of impressions. MSI Center is not needed for any of it.", "Ta podzakładka jako jedyna mierzy, a nie tylko odczytuje. Uruchamia to samo obciążenie procesora i układu graficznego w SILENT, BALANCED i EXTREME i co sekundę zapisuje temperatury, obroty wentylatorów, zegar procesora oraz to, ile pracy procesor faktycznie wykonał. Dzięki temu w zgłoszeniu są liczby zamiast wrażeń. MSI Center nie jest do niczego potrzebny.", "Dieser Unterreiter ist der einzige, der misst und nicht nur ausliest. Er lässt dieselbe Last auf Prozessor und Grafikchip in SILENT, BALANCED und EXTREME laufen und schreibt jede Sekunde Temperaturen, Lüfterdrehzahl, CPU-Takt und die tatsächlich geleistete Arbeit des Prozessors mit. So stehen im Bericht Zahlen statt Eindrücken. MSI Center wird dafür an keiner Stelle gebraucht.", "Ce sous-onglet est le seul à mesurer, et pas seulement à lire. Il exécute la même charge sur le processeur et la puce graphique en SILENT, BALANCED et EXTREME, en relevant chaque seconde les températures, la vitesse des ventilateurs, la fréquence du processeur et le travail que celui-ci a réellement accompli. Ainsi le rapport contient des chiffres et non des impressions. MSI Center n'est nécessaire à aucun moment.", "Esta subpestaña es la única que mide en lugar de solo leer. Ejecuta la misma carga en el procesador y el chip gráfico en SILENT, BALANCED y EXTREME, y registra cada segundo las temperaturas, la velocidad del ventilador, la frecuencia del procesador y cuánto trabajo completó realmente. Así el informe lleva números en lugar de impresiones. No hace falta MSI Center para nada de esto.", "这个子选项卡是唯一进行实测而不只是读取的页面。它在 SILENT、BALANCED 和 EXTREME 下对处理器和显卡运行相同的负载，每秒记录一次温度、风扇转速、处理器频率以及处理器实际完成的工作量。这样报告里给出的是数字，而不是主观感受。整个过程都不需要 MSI Center。", "Esta subguia é a única que mede, em vez de apenas ler. Ela roda a mesma carga no processador e no chip gráfico em SILENT, BALANCED e EXTREME e registra, uma vez por segundo, as temperaturas, a rotação das ventoinhas, a frequência do processador e quanto trabalho ele realmente concluiu. Assim o relatório traz números em vez de impressões. O MSI Center não é necessário para nada disso.", "Эта вкладка единственная, где идёт измерение, а не просто чтение. Она запускает одну и ту же нагрузку на процессор и графический чип в SILENT, BALANCED и EXTREME и раз в секунду записывает температуру, обороты вентилятора, частоту процессора и то, сколько работы процессор реально выполнил. Поэтому в отчёте цифры, а не впечатления. MSI Center для всего этого не нужен." };
        m["pt_warn_write"]    = new[] { "The test writes to the Embedded Controller exactly what picking a profile from the tray menu writes: the handful of values a profile is made of. It sets them in turn for SILENT, BALANCED and EXTREME.", "Test zapisuje do kontrolera EC dokładnie to samo, co wybranie profilu z menu pod ikoną przy zegarku: kilka wartości, z których składa się profil. Ustawia je po kolei dla SILENT, BALANCED i EXTREME.", "Der Test schreibt in den Embedded Controller (EC) genau das, was auch die Wahl eines Profils im Menü unter dem Symbol neben der Uhr schreibt: die paar Werte, aus denen ein Profil besteht. Er setzt sie nacheinander für SILENT, BALANCED und EXTREME.", "Le test écrit dans le contrôleur EC exactement ce qu'écrit le choix d'un profil dans le menu de la barre d'état : les quelques valeurs qui composent un profil. Il les applique tour à tour pour SILENT, BALANCED et EXTREME.", "La prueba escribe en el controlador EC exactamente lo mismo que escribe elegir un perfil en el menú de la bandeja: los pocos valores de los que se compone un perfil. Los fija por turnos para SILENT, BALANCED y EXTREME.", "测试向 EC 写入的内容，和你从托盘菜单选择配置文件时写入的完全一样：构成一个配置文件的那几个值。它会依次为 SILENT、BALANCED 和 EXTREME 设置这些值。", "O teste grava no controlador EC exatamente o mesmo que escolher um perfil no menu da bandeja: os poucos valores que compõem um perfil. Ele os define um de cada vez, para SILENT, BALANCED e EXTREME.", "Тест записывает в контроллер EC ровно то же, что и выбор профиля в меню в трее: несколько значений, из которых состоит профиль. Он задаёт их по очереди для SILENT, BALANCED и EXTREME." };
        m["pt_warn_fourth"]   = new[] { "If your board has a fourth performance mode, the test also writes that one value, checks whether the controller accepted it, and puts it back. The value comes from the model database, it is not guessed.", "Jeśli Twoja płyta ma czwarty tryb wydajności, test wpisze dodatkowo tę jedną wartość, sprawdzi, czy kontroler ją przyjął, i cofnie ją z powrotem. Wartość pochodzi z bazy modeli, nie jest zgadywana.", "Hat deine Platine einen vierten Leistungsmodus, schreibt der Test zusätzlich diesen einen Wert, prüft, ob der EC ihn übernommen hat, und nimmt ihn wieder zurück. Der Wert stammt aus der Modelldatenbank, er ist nicht geraten.", "Si votre carte possède un quatrième mode de performance, le test écrit en plus cette valeur unique, vérifie si le contrôleur l'a acceptée, puis la remet comme avant. La valeur vient de la base de modèles, elle n'est pas devinée.", "Si tu placa tiene un cuarto modo de rendimiento, la prueba escribe además ese único valor, comprueba si el controlador lo aceptó y lo devuelve a como estaba. El valor sale de la base de datos de modelos, no se adivina.", "如果你的主板有第四种性能模式，测试还会额外写入那一个值，检查 EC 是否接受，然后再改回原样。这个值来自机型数据库，不是猜出来的。", "Se a sua placa tiver um quarto modo de desempenho, o teste grava também esse único valor, verifica se o EC o aceitou e o desfaz em seguida. O valor vem do banco de modelos, não é um palpite.", "Если у вашей платы есть четвёртый режим производительности, тест дополнительно запишет это одно значение, проверит, принял ли его контроллер, и вернёт его обратно. Значение берётся из базы моделей, оно не подбирается наугад." };
        m["pt_warn_heat"]    = new[] { "For a minute per profile every processor core runs flat out and the graphics chip runs alongside them, so the laptop gets hot and the fans get loud. That is the measurement, not a fault. Both are loaded because some profiles raise a limit the two chips share. Allow about seven minutes, and leave the machine alone while it runs: anything else you do competes with the test and spoils the numbers.", "Przez minutę na każdy profil wszystkie rdzenie procesora liczą na pełnych obrotach, a razem z nimi pracuje układ graficzny, więc laptop się nagrzeje, a wentylatory będą głośne. Tak wygląda pomiar, to nie usterka. Obciążamy oba, bo niektóre profile podnoszą limit wspólny dla obu układów. Zarezerwuj około siedmiu minut i nie korzystaj w tym czasie z komputera, bo cokolwiek innego robisz, konkuruje z testem i psuje wyniki.", "Pro Profil laufen alle Prozessorkerne eine Minute lang unter Volllast, und der Grafikchip läuft parallel dazu mit, der Laptop wird also heiß und die Lüfter werden laut. Das gehört zur Messung, es ist kein Fehler. Beide werden belastet, weil manche Profile ein Limit anheben, das sich die zwei Chips teilen. Plane etwa sieben Minuten ein und lass das Gerät währenddessen in Ruhe: Alles andere, was du nebenbei machst, konkurriert mit dem Test und verfälscht die Zahlen.", "Pendant une minute par profil, tous les cœurs du processeur tournent à fond et la puce graphique travaille en même temps, la machine chauffe donc et les ventilateurs deviennent bruyants. C'est la mesure, pas un défaut. Les deux sont sollicités parce que certains profils relèvent une limite partagée par les deux puces. Comptez environ sept minutes et ne vous servez pas de la machine pendant le test : tout ce que vous faites d'autre entre en concurrence avec lui et fausse les chiffres.", "Durante un minuto por perfil, todos los núcleos del procesador trabajan al máximo y el chip gráfico trabaja junto a ellos, así que el equipo se calienta y los ventiladores suenan fuerte. Eso es la medición, no un fallo. Se cargan los dos porque algunos perfiles elevan un límite que ambos chips comparten. Reserva unos siete minutos y no uses el equipo mientras tanto: cualquier otra cosa que hagas compite con la prueba y estropea los números.", "每个配置文件下，所有处理器核心都会满载运行一分钟，显卡也会同时满载，因此笔记本会变烫，风扇会变吵。这是测量过程，不是故障。两者都要加载，因为某些配置文件会提高两颗芯片共用的功耗上限。请预留大约七分钟，并在测试运行期间不要使用笔记本：你做的任何其他事情都会与测试争抢资源，让测量结果失真。", "Durante um minuto em cada perfil, todos os núcleos do processador trabalham a plena carga e o chip gráfico trabalha junto com eles, então o notebook esquenta e as ventoinhas ficam barulhentas. Isso é a medição, não um defeito. Os dois são carregados porque alguns perfis elevam um limite que os dois chips compartilham. Reserve cerca de sete minutos e não use o notebook enquanto o teste roda: qualquer outra coisa que você fizer concorre com ele e estraga os números.", "По минуте на каждый профиль все ядра процессора работают на полную, и вместе с ними работает графический чип, поэтому ноутбук нагреется, а вентиляторы станут громкими. Так и выглядит измерение, это не сбой. Нагружаются оба, потому что некоторые профили поднимают предел, общий для двух чипов. Отведите около семи минут и не пользуйтесь компьютером, пока идёт замер: любые ваши действия конкурируют с тестом и портят результаты." };
        m["pt_warn_restore"]  = new[] { "At the end, and when you click Cancel, the profile you had before comes back. Controller settings are volatile, so even if something did go wrong, a restart returns the factory state.", "Na koniec, a także po kliknięciu Anuluj, wraca profil, który miałeś wcześniej. Ustawienia kontrolera są ulotne, więc nawet gdyby coś poszło nie tak, restart przywraca stan fabryczny.", "Am Ende und auch beim Klick auf Abbrechen kommt das Profil zurück, das du vorher hattest. Die Einstellungen im EC sind flüchtig, und selbst wenn wirklich etwas schiefgehen sollte, stellt ein Neustart den Werkszustand wieder her.", "À la fin, comme lorsque vous cliquez sur Annuler, le profil que vous aviez avant revient. Les réglages du contrôleur sont volatils, donc même si quelque chose se passait mal, un redémarrage rétablit l'état d'usine.", "Al final, y también cuando pulsas Cancelar, vuelve el perfil que tenías antes. Los ajustes del controlador son volátiles, así que aunque algo saliera mal, un reinicio devuelve el estado de fábrica.", "测试结束时会恢复你之前使用的配置文件，点击取消时也一样。EC 中的设置是易失的，所以即使真出了问题，重启后也会回到出厂状态。", "No fim, e também quando você clica em Cancelar, o perfil que você tinha antes volta. As gravações no EC são voláteis, então, mesmo que algo desse errado, reiniciar devolve o estado de fábrica.", "В конце, а также когда вы нажимаете Отмена, возвращается профиль, который был у вас раньше. Настройки контроллера не сохраняются, поэтому даже если что-то пойдёт не так, перезагрузка вернёт заводское состояние." };
        m["st_gpu_clock"] = new[] { "GPU clock", "Zegar GPU", "GPU-Takt", "Fréquence GPU", "Frecuencia GPU", "GPU 频率", "Clock da GPU", "Частота GPU" };
        // {0} adapter name, {1} current MHz, {2} ceiling MHz, {3} percent of the ceiling
        m["st_gpu_clock_tip"] = new[] {
            "{0}\n\nCore clock {1} MHz of a {2} MHz ceiling, so {3} % of what this card can run at. A busy card sitting well under its ceiling is the firmware holding it there, which is exactly what a performance profile changes. Read from Windows itself, with no vendor software installed.",
            "{0}\n\nZegar rdzenia {1} MHz przy pułapie {2} MHz, czyli {3} % tego, co ta karta potrafi. Obciążona karta trzymająca się wyraźnie poniżej pułapu to firmware, który ją tam przytrzymuje, a właśnie to zmienia profil wydajności. Odczyt prosto z Windows, bez żadnego oprogramowania producenta.",
            "{0}\n\nKerntakt {1} MHz bei einer Obergrenze von {2} MHz, also {3} % dessen, was diese Karte leisten kann. Eine ausgelastete Karte deutlich unter ihrer Obergrenze wird von der Firmware dort gehalten, und genau das ändert ein Leistungsprofil. Direkt von Windows gelesen, ohne Hersteller-Software.",
            "{0}\n\nFréquence du cœur {1} MHz pour un plafond de {2} MHz, soit {3} % de ce que cette carte sait faire. Une carte chargée qui reste nettement sous son plafond y est maintenue par le firmware, et c'est précisément ce que change un profil de performance. Lu directement depuis Windows, sans logiciel du constructeur.",
            "{0}\n\nFrecuencia del núcleo {1} MHz frente a un techo de {2} MHz, es decir el {3} % de lo que esta tarjeta puede dar. Una tarjeta ocupada que se queda muy por debajo de su techo está retenida ahí por el firmware, que es justo lo que cambia un perfil de rendimiento. Leído directamente de Windows, sin software del fabricante.",
            "{0}\n\n核心频率 {1} MHz，上限 {2} MHz，即这张显卡能力的 {3} %。显卡满载却明显低于上限，说明是固件把它压在那里，而这正是性能配置文件所改变的。数据直接来自 Windows，无需安装厂商软件。",
            "{0}\n\nClock do núcleo {1} MHz para um teto de {2} MHz, ou seja {3} % do que esta placa consegue. Uma placa ocupada que fica bem abaixo do teto está sendo segurada ali pelo firmware, que é exatamente o que um perfil de desempenho altera. Lido direto do Windows, sem software do fabricante.",
            "{0}\n\nЧастота ядра {1} МГц при потолке {2} МГц, то есть {3} % от возможностей этой карты. Загруженная карта, держащаяся заметно ниже потолка, удерживается там прошивкой, а именно это и меняет профиль производительности. Читается прямо из Windows, без ПО производителя." };
        // {0} adapter name - shown when the card has powered itself down and reports no clock
        m["st_gpu_clock_tip_idle"] = new[] {
            "{0}\n\nNo clock to report: with nothing asking the card for work it powers itself down, and a sleeping card does not answer. Start anything that uses it and the figure appears. Read from Windows itself, with no vendor software installed.",
            "{0}\n\nBrak zegara do pokazania: gdy nic nie prosi karty o pracę, ona się wyłącza, a śpiąca karta nie odpowiada. Uruchom cokolwiek, co jej używa, a wartość się pojawi. Odczyt prosto z Windows, bez żadnego oprogramowania producenta.",
            "{0}\n\nKein Takt zu melden: Wenn nichts Arbeit von der Karte verlangt, schaltet sie sich ab, und eine schlafende Karte antwortet nicht. Starte etwas, das sie nutzt, und der Wert erscheint. Direkt von Windows gelesen, ohne Hersteller-Software.",
            "{0}\n\nAucune fréquence à afficher : quand rien ne demande de travail à la carte, elle s'éteint, et une carte endormie ne répond pas. Lancez quelque chose qui l'utilise et la valeur apparaîtra. Lu directement depuis Windows, sans logiciel du constructeur.",
            "{0}\n\nNo hay frecuencia que mostrar: si nada le pide trabajo, la tarjeta se apaga, y una tarjeta dormida no responde. Inicia algo que la use y el valor aparecerá. Leído directamente de Windows, sin software del fabricante.",
            "{0}\n\n没有可显示的频率：没有任何程序请求显卡工作时，它会自行关闭，而休眠的显卡不会应答。启动任何使用它的程序，数值就会出现。数据直接来自 Windows，无需安装厂商软件。",
            "{0}\n\nSem clock para mostrar: quando nada pede trabalho à placa, ela se desliga, e uma placa dormindo não responde. Inicie algo que a use e o valor aparece. Lido direto do Windows, sem software do fabricante.",
            "{0}\n\nЧастоту показать нечего: когда карту никто не нагружает, она отключается, а спящая карта не отвечает. Запустите что-нибудь, что её использует, и значение появится. Читается прямо из Windows, без ПО производителя." };
        // ---- Report start screen ----
        m["rep_home_title"] = new[] {
            "Three tests, three different questions",
            "Trzy testy, trzy różne pytania",
            "Drei Tests, drei verschiedene Fragen",
            "Trois tests, trois questions différentes",
            "Tres pruebas, tres preguntas distintas",
            "三个测试，三个不同的问题",
            "Três testes, três perguntas diferentes",
            "Три теста, три разных вопроса" };
        m["rep_home_intro1"] = new[] {
            "GhostDeck switches profiles by writing a handful of values to the laptop's embedded controller, the small chip that runs the fans and the power limits. Those values differ between boards, so for a laptop nobody has reported yet we simply do not know them, and the app stays read-only until someone establishes them.",
            "GhostDeck przełącza profile, zapisując kilka wartości do kontrolera wbudowanego w laptopa, czyli małego układu, który steruje wentylatorami i limitami mocy. Te wartości różnią się między płytami, więc dla laptopa, którego nikt jeszcze nie zgłosił, po prostu ich nie znamy, i aplikacja pozostaje w trybie tylko do odczytu, dopóki ktoś ich nie ustali.",
            "GhostDeck schaltet Profile um, indem es einige Werte in den eingebetteten Controller des Laptops schreibt, den kleinen Chip, der Lüfter und Leistungsgrenzen steuert. Diese Werte unterscheiden sich je nach Board; für einen Laptop, den noch niemand gemeldet hat, kennen wir sie schlicht nicht, und die App bleibt schreibgeschützt, bis jemand sie ermittelt.",
            "GhostDeck change de profil en écrivant quelques valeurs dans le contrôleur embarqué du portable, la petite puce qui gère les ventilateurs et les limites de puissance. Ces valeurs diffèrent d'une carte à l'autre : pour un portable que personne n'a encore signalé, nous ne les connaissons tout simplement pas, et l'application reste en lecture seule tant que quelqu'un ne les a pas établies.",
            "GhostDeck cambia de perfil escribiendo unos pocos valores en el controlador integrado del portátil, el pequeño chip que gobierna los ventiladores y los límites de potencia. Esos valores difieren entre placas, así que para un portátil que nadie ha reportado aún simplemente no los conocemos, y la aplicación permanece en solo lectura hasta que alguien los establezca.",
            "GhostDeck 通过向笔记本的嵌入式控制器（管理风扇和功率限制的小芯片）写入几个值来切换配置文件。这些值因主板而异，对于还没有人报告过的笔记本，我们根本不知道它们，应用会保持只读，直到有人确定这些值。",
            "O GhostDeck troca de perfil gravando alguns valores no controlador embutido do notebook, o pequeno chip que comanda as ventoinhas e os limites de energia. Esses valores variam entre placas; para um notebook que ninguém reportou ainda, simplesmente não os conhecemos, e o app fica somente leitura até alguém estabelecê-los.",
            "GhostDeck переключает профили, записывая несколько значений во встроенный контроллер ноутбука, маленький чип, управляющий вентиляторами и лимитами мощности. Эти значения различаются между платами, поэтому для ноутбука, о котором ещё никто не сообщил, мы их просто не знаем, и приложение остаётся в режиме только чтения, пока кто-нибудь их не установит." };
        m["rep_home_intro2"] = new[] {
            "These three tests are how that happens. The first two work by comparison: you switch scenarios in MSI Center while the wizard watches what changes in the controller, which is why they need MSI Center installed as an independent reference. The third needs nothing: it runs the same load in every profile and counts how much work your machine actually got done, so it answers whether a profile changes anything without asking you to judge by ear.",
            "Te trzy testy służą właśnie do tego. Dwa pierwsze działają przez porównanie: Ty przełączasz scenariusze w MSI Center, a kreator patrzy, co zmienia się w kontrolerze. Dlatego wymagają zainstalowanego MSI Center, jako niezależnego punktu odniesienia. Trzeci nie wymaga niczego: uruchamia w każdym profilu to samo obciążenie i liczy, ile pracy Twój komputer faktycznie wykonał, więc odpowiada na pytanie, czy profil cokolwiek zmienia, bez proszenia Cię o ocenę na słuch.",
            "Genau dafür sind diese drei Tests da. Die ersten beiden arbeiten per Vergleich: Du schaltest Szenarien in MSI Center um, während der Assistent beobachtet, was sich im Controller ändert. Deshalb brauchen sie ein installiertes MSI Center als unabhängige Referenz. Der dritte braucht nichts: Er fährt in jedem Profil dieselbe Last und zählt, wie viel Arbeit der Rechner tatsächlich geschafft hat. So beantwortet er, ob ein Profil überhaupt etwas ändert, ohne dass du nach Gehör urteilen musst.",
            "C'est exactement à cela que servent ces trois tests. Les deux premiers fonctionnent par comparaison : vous changez de scénario dans MSI Center pendant que l'assistant observe ce qui change dans le contrôleur ; ils exigent donc MSI Center installé comme référence indépendante. Le troisième n'exige rien : il exécute la même charge dans chaque profil et compte le travail réellement accompli, répondant ainsi à la question de savoir si un profil change quoi que ce soit, sans vous demander de juger à l'oreille.",
            "Para eso están estas tres pruebas. Las dos primeras funcionan por comparación: tú cambias de escenario en MSI Center mientras el asistente observa qué cambia en el controlador; por eso necesitan MSI Center instalado como referencia independiente. La tercera no necesita nada: ejecuta la misma carga en cada perfil y cuenta cuánto trabajo hizo realmente tu máquina, así que responde si un perfil cambia algo sin pedirte juzgar de oído.",
            "这三个测试正是为此而生。前两个通过对比工作：你在 MSI Center 中切换场景，向导观察控制器中发生的变化，因此它们需要安装 MSI Center 作为独立参照。第三个则无需任何东西：它在每个配置文件下运行相同的负载并统计电脑实际完成了多少工作，从而回答配置文件是否真的改变了什么，而不用你凭耳朵判断。",
            "Esses três testes existem exatamente para isso. Os dois primeiros funcionam por comparação: você troca de cenário no MSI Center enquanto o assistente observa o que muda no controlador, e por isso exigem o MSI Center instalado como referência independente. O terceiro não exige nada: roda a mesma carga em cada perfil e conta quanto trabalho a máquina realmente concluiu, respondendo se um perfil muda alguma coisa sem pedir que você julgue de ouvido.",
            "Именно для этого и нужны эти три теста. Первые два работают через сравнение: вы переключаете сценарии в MSI Center, а мастер смотрит, что меняется в контроллере, поэтому им нужен установленный MSI Center как независимый ориентир. Третьему не нужно ничего: он запускает одну и ту же нагрузку в каждом профиле и считает, сколько работы машина реально выполнила, отвечая, меняет ли профиль хоть что-то, не прося вас судить на слух." };
        m["rep_home_intro3"] = new[] {
            "Not recognised yet? Go left to right: Profiles first, then Fan curve, then the Power test. Already supported? Then the Power test alone is enough - it proves the profiles do what they claim on your board.",
            "Twojego laptopa nie ma jeszcze na liście? Idź od lewej: najpierw Profile, potem Krzywa wentylatora, na koniec Test mocy. Laptop jest już obsługiwany? Wtedy wystarczy sam Test mocy - potwierdzi, że profile robią na Twojej płycie to, co obiecują.",
            "Wird dein Laptop noch nicht erkannt? Gehe von links nach rechts: erst Profile, dann Lüfterkurve, zum Schluss der Leistungstest. Wird er bereits unterstützt? Dann genügt der Leistungstest allein - er belegt, dass die Profile auf deinem Board tun, was sie versprechen.",
            "Votre portable n'est pas encore reconnu ? Allez de gauche à droite : d'abord Profils, puis Courbe du ventilateur, et enfin le Test de puissance. Déjà pris en charge ? Alors le Test de puissance suffit à lui seul - il prouve que les profils font sur votre carte ce qu'ils promettent.",
            "¿Tu portátil aún no está reconocido? Ve de izquierda a derecha: primero Perfiles, luego Curva del ventilador y al final la Prueba de potencia. ¿Ya está soportado? Entonces basta la Prueba de potencia: demuestra que los perfiles hacen en tu placa lo que prometen.",
            "你的笔记本还未被识别？请从左到右依次进行：先是配置文件，然后是风扇曲线，最后是功率测试。已经受支持？那么只需运行功率测试，它能证明配置文件在你的主板上确实名副其实。",
            "Seu notebook ainda não é reconhecido? Vá da esquerda para a direita: primeiro Perfis, depois Curva da ventoinha e por fim o Teste de potência. Já é suportado? Então basta o Teste de potência - ele prova que os perfis fazem na sua placa o que prometem.",
            "Ваш ноутбук ещё не распознан? Идите слева направо: сначала Профили, затем Кривая вентилятора, в конце Тест мощности. Уже поддерживается? Тогда достаточно одного Теста мощности - он докажет, что профили действительно делают на вашей плате то, что обещают." };
        m["rep_home_q1"] = new[] {
            "What does MSI Center write for each of its scenarios?",
            "Co MSI Center zapisuje dla każdego ze swoich scenariuszy?",
            "Was schreibt MSI Center für jedes seiner Szenarien?",
            "Qu'écrit MSI Center pour chacun de ses scénarios ?",
            "¿Qué escribe MSI Center para cada uno de sus escenarios?",
            "MSI Center 为它的每个场景写入了什么？",
            "O que o MSI Center grava para cada um dos seus cenários?",
            "Что MSI Center записывает для каждого из своих сценариев?" };
        m["rep_home_d1"] = new[] {
            "Reads the controller once per scenario while you switch between them. This is how a model that nobody has reported yet gets its profile recipe.",
            "Odczytuje kontroler raz na scenariusz, podczas gdy Ty je przełączasz. Tak model, którego nikt jeszcze nie zgłosił, dostaje swój przepis na profile.",
            "Liest den Controller einmal pro Szenario, während du sie umschaltest. So bekommt ein Modell, das noch niemand gemeldet hat, sein Profilrezept.",
            "Lit le contrôleur une fois par scénario pendant que vous les changez. C'est ainsi qu'un modèle que personne n'a encore signalé obtient sa recette de profils.",
            "Lee el controlador una vez por escenario mientras tú los cambias. Así un modelo que nadie ha reportado aún obtiene su receta de perfiles.",
            "在你切换场景时，每个场景读取一次控制器。尚未有人报告过的机型就是这样获得它的配置方案的。",
            "Lê o controlador uma vez por cenário enquanto você os troca. É assim que um modelo que ninguém reportou ainda ganha a sua receita de perfis.",
            "Считывает контроллер по одному разу на сценарий, пока вы их переключаете. Так модель, о которой ещё никто не сообщил, получает свой рецепт профилей." };
        m["rep_home_q2"] = new[] {
            "Where does this board keep its fan curve tables?",
            "Gdzie ta płyta trzyma tablice krzywej wentylatora?",
            "Wo hält dieses Board seine Lüfterkurven-Tabellen?",
            "Où cette carte range-t-elle ses tables de courbe de ventilateur ?",
            "¿Dónde guarda esta placa sus tablas de curva del ventilador?",
            "这块主板把风扇曲线表放在哪里？",
            "Onde esta placa guarda as tabelas da curva da ventoinha?",
            "Где эта плата хранит таблицы кривой вентилятора?" };
        m["rep_home_d2"] = new[] {
            "You set a curve with distinctive speeds in MSI Center, and the wizard finds those speeds in the controller. That confirms the addresses before this app ever writes a curve of its own.",
            "Ustawiasz w MSI Center krzywą o charakterystycznych prędkościach, a kreator odnajduje te prędkości w kontrolerze. To potwierdza adresy, zanim ta aplikacja sama cokolwiek zapisze.",
            "Du stellst in MSI Center eine Kurve mit markanten Drehzahlen ein, und der Assistent findet diese Werte im Controller wieder. Das bestätigt die Adressen, bevor diese App je eine eigene Kurve schreibt.",
            "Vous réglez dans MSI Center une courbe aux vitesses distinctives, et l'assistant retrouve ces vitesses dans le contrôleur. Cela confirme les adresses avant que cette application n'écrive la moindre courbe.",
            "Configuras en MSI Center una curva con velocidades distintivas y el asistente encuentra esas velocidades en el controlador. Eso confirma las direcciones antes de que esta aplicación escriba curva alguna.",
            "你在 MSI Center 中设置一条转速特征明显的曲线，向导在控制器中找到这些转速。这样就在本应用写入任何曲线之前确认了地址。",
            "Você define no MSI Center uma curva com velocidades características, e o assistente encontra essas velocidades no controlador. Isso confirma os endereços antes de este app gravar qualquer curva própria.",
            "Вы задаёте в MSI Center кривую с характерными скоростями, а мастер находит эти скорости в контроллере. Это подтверждает адреса до того, как приложение запишет собственную кривую." };
        m["rep_home_q3"] = new[] {
            "Does what we write actually change anything?",
            "Czy to, co zapisujemy, faktycznie coś zmienia?",
            "Ändert das, was wir schreiben, tatsächlich etwas?",
            "Ce que nous écrivons change-t-il réellement quelque chose ?",
            "¿Lo que escribimos cambia realmente algo?",
            "我们写入的值真的改变了什么吗？",
            "O que gravamos muda alguma coisa de verdade?",
            "Меняет ли то, что мы записываем, хоть что-нибудь?" };
        m["rep_home_d3"] = new[] {
            "Runs the same load on the processor and the graphics chip in each profile and counts the work completed, so the answer is a number rather than an impression. It also measures the baseline twice, so a run that drifted says so instead of pretending.",
            "Uruchamia w każdym profilu to samo obciążenie procesora i układu graficznego i liczy wykonaną pracę, więc odpowiedzią jest liczba, a nie wrażenie. Mierzy też punkt odniesienia dwa razy, więc przebieg, który odjechał, sam to mówi, zamiast udawać.",
            "Fährt in jedem Profil dieselbe Last auf Prozessor und Grafikchip und zählt die erledigte Arbeit; die Antwort ist eine Zahl, kein Eindruck. Die Basislinie wird zweimal gemessen, sodass ein verlaufener Durchgang das selbst sagt, statt etwas vorzutäuschen.",
            "Exécute la même charge sur le processeur et la puce graphique dans chaque profil et compte le travail accompli : la réponse est un nombre, pas une impression. La référence est mesurée deux fois, si bien qu'un passage qui a dérivé le dit lui-même au lieu de faire semblant.",
            "Ejecuta la misma carga en el procesador y el chip gráfico en cada perfil y cuenta el trabajo completado: la respuesta es un número, no una impresión. Además mide la referencia dos veces, así que una pasada que derivó lo dice ella misma en vez de fingir.",
            "在每个配置文件下对处理器和显卡运行相同的负载并统计完成的工作量，答案是一个数字而不是主观感受。基准还会测量两次，跑偏的一次会自己说明，而不是装作正常。",
            "Roda a mesma carga no processador e no chip gráfico em cada perfil e conta o trabalho concluído: a resposta é um número, não uma impressão. A linha de base é medida duas vezes, então uma execução que derivou diz isso ela mesma em vez de fingir.",
            "Запускает в каждом профиле одну и ту же нагрузку на процессор и графический чип и считает выполненную работу: ответом становится число, а не впечатление. Базовая линия измеряется дважды, поэтому уплывший прогон сам об этом говорит, а не притворяется." };
        m["rep_home_f_read"] = new[] {
            "Needs MSI Center · read-only, writes nothing",
            "Wymaga MSI Center · tylko odczyt, nic nie zapisuje",
            "Braucht MSI Center · nur Lesen, schreibt nichts",
            "Exige MSI Center · lecture seule, n'écrit rien",
            "Necesita MSI Center · solo lectura, no escribe nada",
            "需要 MSI Center · 只读，不写入任何内容",
            "Exige o MSI Center · somente leitura, não grava nada",
            "Нужен MSI Center · только чтение, ничего не записывает" };
        m["rep_home_f_power"] = new[] {
            "Needs nothing · writes profile values, restores them at the end",
            "Nie wymaga niczego · zapisuje wartości profili i przywraca je na końcu",
            "Braucht nichts · schreibt Profilwerte und stellt sie am Ende wieder her",
            "N'exige rien · écrit les valeurs des profils et les restaure à la fin",
            "No necesita nada · escribe valores de perfil y los restaura al final",
            "无需任何东西 · 写入配置文件的值并在结束时恢复",
            "Não exige nada · grava valores de perfil e os restaura no final",
            "Ничего не нужно · записывает значения профилей и в конце их восстанавливает" };
        m["pt_writes"]        = new[] { "Addresses the test may write", "Adresy, które test może zapisać", "Mögliche Schreibadressen", "Adresses écrites possibles", "Direcciones que la prueba puede escribir", "测试可能写入的地址", "Endereços que o teste pode gravar", "Адреса возможной записи" };
        m["pt_steps"]          = new[] { "What runs, in order", "Kolejne kroki", "Ablauf, der Reihe nach", "Étapes, dans l'ordre", "Qué se ejecuta, en orden", "执行顺序", "O que roda, em ordem", "Порядок шагов" };
        m["pt_row_restore"]    = new[] { "Restore your profile", "Przywróć profil", "Profil wiederherstellen", "Restaurer votre profil", "Restaurar tu perfil", "恢复你的配置文件", "Restaurar seu perfil", "Возврат профиля" };
        m["pt_consent"]        = new[] { "I understand the machine will run hot and loud for a few minutes", "Rozumiem, że przez kilka minut laptop będzie gorący i głośny", "Mir ist klar, dass das Gerät ein paar Minuten heiß und laut läuft", "Je comprends que la machine va chauffer et être bruyante pendant quelques minutes", "Entiendo que el equipo se calentará y sonará fuerte durante unos minutos", "我知道机器会在几分钟内变烫且噪音变大", "Entendo que o notebook vai esquentar e ficar barulhento por alguns minutos", "Понимаю, что несколько минут ноутбук будет горячим и шумным" };
        m["pt_need_consent"]   = new[] { "Tick the box above first.", "Najpierw zaznacz pole powyżej.", "Setze zuerst den Haken oben.", "Cochez d'abord la case ci-dessus.", "Marca primero la casilla de arriba.", "请先勾选上面的复选框。", "Marque a caixa acima primeiro.", "Сначала отметьте галочку выше." };
        m["pt_start"]          = new[] { "Start the test", "Uruchom test", "Test starten", "Lancer le test", "Iniciar la prueba", "开始测试", "Iniciar o teste", "Запустить тест" };
        m["pt_cancel"]         = new[] { "Cancel", "Anuluj", "Abbrechen", "Annuler", "Cancelar", "取消", "Cancelar", "Отмена" };
        m["pt_stage_settle"]   = new[] { "Settling", "Stabilizacja", "Einpendeln", "Stabilisation", "Estabilizando", "稳定中", "Estabilizando", "Стабилизация" };
        m["pt_stage_load"]     = new[] { "Under load", "Pod obciążeniem", "Unter Last", "Sous charge", "Bajo carga", "负载中", "Sob carga", "Под нагрузкой" };
        m["pt_stage_write"]    = new[] { "Writing the extra value", "Zapis dodatkowej wartości", "Zusatzwert schreiben", "Écriture de la 4e valeur", "Escribiendo el valor extra", "写入额外值", "Gravando o valor extra", "Запись доп. значения" };
        m["pt_stage_revert"]   = new[] { "Putting the register back", "Przywracanie rejestru", "Register zurücksetzen", "Restauration du registre", "Restaurando el registro", "还原寄存器", "Restaurando o registrador", "Возврат регистра" };
        m["pt_stage_read"]     = new[] { "Reading the controller", "Odczyt EC", "EC lesen", "Lecture de l'EC", "Leyendo el EC", "读取 EC", "Lendo o EC", "Чтение EC" };
        m["pt_stage_check"]  = new[] { "Checking the machine is idle", "Sprawdzanie, czy komputer jest bezczynny", "Leerlauf prüfen", "Vérification de l'inactivité", "Comprobando inactividad", "检查是否空闲", "Verificando se está ocioso", "Проверка простоя" };
        m["pt_block_sim"]      = new[] { "Preview mode is on, so nothing is written to the controller. The test needs real hardware.", "Tryb podglądu jest włączony, więc nic nie jest zapisywane do EC. Test wymaga prawdziwego sprzętu.", "Der Vorschaumodus ist aktiv, es wird also nichts in den EC geschrieben. Der Test braucht echte Hardware.", "Le mode aperçu est actif, rien n'est écrit dans l'EC. Le test exige du matériel réel.", "El modo de vista previa está activado, así que no se escribe nada en el EC. La prueba necesita hardware real.", "预览模式已开启，不会向 EC 写入任何内容。此测试需要真实硬件。", "O modo de pré-visualização está ativo, então nada é gravado no EC. O teste precisa de hardware real.", "Включён режим предпросмотра, в EC ничего не записывается. Тесту нужно реальное железо." };
        m["pt_block_unknown"]  = new[] { "This firmware is not in the model database, so there are no register addresses to test.", "Tego firmware'u nie ma w bazie modeli, więc nie ma adresów rejestrów do przetestowania.", "Diese Firmware steht nicht in der Modelldatenbank, es gibt also keine Registeradressen zum Testen.", "Ce firmware ne figure pas dans la base de modèles, il n'y a donc aucune adresse de registre à tester.", "Este firmware no está en la base de datos de modelos, así que no hay direcciones de registro que probar.", "该固件不在机型数据库中，因此没有可供测试的寄存器地址。", "Este firmware não está no banco de modelos, então não há endereços de registradores para testar.", "Этой прошивки нет в базе моделей, поэтому нет адресов регистров для теста." };
        m["pt_block_locked"]   = new[] { "Your model is experimental. Turn on experimental writes in Settings first.", "Twój model jest eksperymentalny. Najpierw włącz zapisy eksperymentalne w Ustawieniach.", "Dein Modell ist experimentell. Aktiviere zuerst experimentelle Schreibvorgänge in den Einstellungen.", "Votre modèle est expérimental. Activez d'abord les écritures expérimentales dans Paramètres.", "Tu modelo es experimental. Activa primero las escrituras experimentales en Ajustes.", "你的机型为实验性。请先在设置中启用实验性写入。", "Seu modelo é experimental. Ative primeiro as gravações experimentais nas Configurações.", "Ваша модель экспериментальная. Сначала включите экспериментальную запись в настройках." };
        m["pt_block_battery"]  = new[] { "Plug the charger in. On battery the firmware caps power by itself, which would make every measurement meaningless.", "Podłącz zasilacz. Na baterii firmware sam ogranicza moc, więc każdy pomiar byłby bezwartościowy.", "Schließe das Netzteil an. Im Akkubetrieb begrenzt die Firmware die Leistung von sich aus, jede Messung wäre damit wertlos.", "Branchez le chargeur. Sur batterie, le firmware limite lui-même la puissance, ce qui rendrait toute mesure dénuée de sens.", "Conecta el cargador. Con batería, el firmware limita la potencia por su cuenta, lo que dejaría sin sentido todas las mediciones.", "请接上电源适配器。使用电池时固件会自行限制功耗，任何测量都会失去意义。", "Conecte o carregador. Na bateria o firmware limita a potência por conta própria, o que tornaria toda medição sem sentido.", "Подключите зарядное устройство. От батареи прошивка сама ограничивает мощность, и все измерения потеряют смысл." };
        m["pt_res_busy"]    = new[] { "Something else was using the processor, so these numbers compare the machine's other work, not the profiles. Run it again on an idle machine.", "Coś innego korzystało z procesora, więc te liczby porównują obcą pracę, a nie profile. Powtórz test na bezczynnym komputerze.", "Etwas anderes hat den Prozessor ausgelastet, diese Zahlen vergleichen also fremde Arbeit und nicht die Profile. Wiederhole den Test auf einem unbelasteten Rechner.", "Un autre programme utilisait le processeur, ces chiffres comparent donc l'autre activité de la machine, pas les profils. Relancez le test sur une machine au repos.", "Otra cosa estaba usando el procesador, así que estos números comparan ese otro trabajo del equipo, no los perfiles. Repite la prueba con el equipo inactivo.", "其他程序占用了处理器，因此这些数字比较的是机器上的其他工作，而不是配置文件。请在空闲的机器上重新运行一次。", "Outra coisa estava usando o processador, então estes números comparam o outro trabalho da máquina, não os perfis. Repita o teste com a máquina ociosa.", "Процессор был занят чем-то ещё, поэтому эти числа сравнивают постороннюю нагрузку, а не профили. Повторите тест на простаивающем компьютере." };
        m["pt_res_done"]       = new[] { "Done. The full report is on your clipboard and saved to a file.", "Gotowe. Pełny raport jest w schowku i zapisany do pliku.", "Fertig. Der vollständige Bericht liegt in der Zwischenablage und wurde als Datei gespeichert.", "Terminé. Le rapport complet est dans le presse-papiers et enregistré dans un fichier.", "Listo. El informe completo está en el portapapeles y guardado en un archivo.", "完成。完整报告已复制到剪贴板并保存为文件。", "Pronto. O relatório completo está na área de transferência e salvo em arquivo.", "Готово. Полный отчёт скопирован в буфер обмена и сохранён в файл." };
        m["pt_res_aborted"]    = new[] { "Stopped: {0}. Whatever was measured is in the report.", "Zatrzymano: {0}. To, co zdążyliśmy zmierzyć, jest w raporcie.", "Abgebrochen: {0}. Was gemessen wurde, steht im Bericht.", "Arrêté : {0}. Ce qui a été mesuré figure dans le rapport.", "Detenido: {0}. Lo que se llegó a medir está en el informe.", "已停止：{0}。已测得的数据都在报告中。", "Interrompido: {0}. O que foi medido está no relatório.", "Остановлено: {0}. Всё, что успели измерить, есть в отчёте." };
        m["pt_res_prebusy"]  = new[] { "The machine was already {0} % busy, so the test refused to start and wrote nothing. Close what is running, wait a moment and try again.", "Komputer był już zajęty w {0} %, więc test nie wystartował i niczego nie zapisał. Zamknij, co masz uruchomione, odczekaj chwilę i spróbuj ponownie.", "Der Rechner war bereits zu {0} % ausgelastet, deshalb ist der Test gar nicht erst gestartet und hat nichts geschrieben. Schließe, was gerade läuft, warte einen Moment und versuche es erneut.", "La machine était déjà occupée à {0} %, le test n'a donc pas démarré et n'a rien écrit. Fermez ce qui tourne, patientez un instant et réessayez.", "El equipo ya estaba al {0} % de uso, así que la prueba no llegó a arrancar y no escribió nada. Cierra lo que tengas abierto, espera un momento e inténtalo de nuevo.", "机器当时已有 {0}% 的负载，因此测试拒绝启动，也没有写入任何内容。请关闭正在运行的程序，稍等片刻后再试一次。", "A máquina já estava {0} % ocupada, então o teste não começou e não gravou nada. Feche o que estiver rodando, espere um pouco e tente de novo.", "Компьютер уже был занят на {0} %, поэтому тест не запустился и ничего не записал. Закройте запущенные программы, подождите немного и попробуйте снова." };
        m["pt_res_refused"]    = new[] { "The controller refused {0} from outside. That is the answer we needed.", "EC odrzucił wartość {0} zapisaną z zewnątrz. Tej odpowiedzi szukaliśmy.", "Der EC hat {0} von außen abgelehnt. Genau das wollten wir wissen.", "L'EC a refusé {0} depuis l'extérieur. C'est la réponse que nous cherchions.", "El EC rechazó {0} desde fuera. Esa es la respuesta que necesitábamos.", "EC 拒绝了来自外部的 {0}。这正是我们需要的答案。", "O EC recusou {0} vindo de fora. Essa é a resposta que precisávamos.", "EC отклонил запись {0} извне. Это и есть нужный нам ответ." };
        m["pt_res_accepted"]   = new[] { "{0} was accepted and cleared cleanly.", "Wartość {0} została przyjęta i czysto wycofana.", "{0} wurde übernommen und sauber zurückgesetzt.", "{0} a été accepté et effacé proprement.", "{0} se aceptó y se limpió correctamente.", "{0} 已被接受，并已干净地清除。", "{0} foi aceito e zerado corretamente.", "Запись {0} принята и корректно сброшена." };
        m["pt_res_stuck"]      = new[] { "{0} was accepted but did not clear. The report shows what is still set.", "Wartość {0} została przyjęta, ale nie wycofała się. W raporcie widać, co nadal jest ustawione.", "{0} wurde übernommen, aber nicht zurückgesetzt. Der Bericht zeigt, was noch gesetzt ist.", "{0} a été accepté mais n'a pas été effacé. Le rapport indique ce qui reste actif.", "{0} se aceptó pero no se limpió. El informe muestra lo que sigue activo.", "{0} 已被接受但未清除。报告中列出了仍处于设置状态的内容。", "{0} foi aceito, mas não foi zerado. O relatório mostra o que continua definido.", "Запись {0} принята, но не сбросилась. В отчёте видно, что осталось установленным." };
    }
}
