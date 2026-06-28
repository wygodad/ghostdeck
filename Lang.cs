namespace MSIProfileSwitcher;

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

    private static readonly Dictionary<string, string[]> Map = new()
    {
        ["menu_settings"]  = new[] { "Settings", "Ustawienia", "Einstellungen", "Paramètres", "Configuración", "设置", "Configurações", "Настройки" },
        ["menu_status"]    = new[] { "Status", "Status", "Status", "Statut", "Estado", "状态", "Status", "Состояние" },
        ["menu_panel"]     = new[] { "Open panel", "Otwórz panel", "Panel öffnen", "Ouvrir le panneau", "Abrir panel", "打开面板", "Abrir painel", "Открыть панель" },
        ["tab_scenarios"]  = new[] { "Scenarios", "Scenariusze", "Szenarien", "Scénarios", "Escenarios", "场景", "Cenários", "Сценарии" },
        ["tab_updates"]    = new[] { "Updates", "Aktualizacje", "Updates", "Mises à jour", "Actualizaciones", "更新", "Atualizações", "Обновления" },
        ["scen_title"]     = new[] { "Choose a scenario", "Wybierz scenariusz", "Szenario wählen", "Choisir un scénario", "Elige un escenario", "选择场景", "Escolha um cenário", "Выберите сценарий" },
        ["scen_autoswitch"]= new[] { "Auto-switch on AC / battery", "Auto-przełączanie AC / bateria", "Auto-Wechsel bei Netz / Akku", "Bascule auto secteur / batterie", "Cambio automático CA / batería", "接通/电池自动切换", "Troca automática CA / bateria", "Авто-переключение сеть / батарея" },
        ["gen_off"]        = new[] { "Off", "Wyłączone", "Aus", "Désactivé", "Apagado", "关闭", "Desligado", "Выкл" },
        ["gen_off_short"]  = new[] { "Off", "Wył.", "Aus", "Off", "No", "关", "Não", "Выкл" },
        ["upd_installed"]  = new[] { "Installed version", "Zainstalowana wersja", "Installierte Version", "Version installée", "Versión instalada", "已安装版本", "Versão instalada", "Установленная версия" },
        ["upd_latest_ok"]  = new[] { "You're on the latest version", "Używasz najnowszej wersji", "Sie nutzen die neueste Version", "Vous avez la dernière version", "Tienes la última versión", "已是最新版本", "Você está na versão mais recente", "У вас последняя версия" },
        ["upd_check_now"]  = new[] { "Check now", "Sprawdź teraz", "Jetzt prüfen", "Vérifier maintenant", "Comprobar ahora", "立即检查", "Verificar agora", "Проверить сейчас" },
        ["upd_checking"]   = new[] { "Checking…", "Sprawdzanie…", "Wird geprüft…", "Vérification…", "Comprobando…", "正在检查…", "Verificando…", "Проверка…" },
        ["upd_last_checked"] = new[] { "Last checked: {0}", "Ostatnio sprawdzano: {0}", "Zuletzt geprüft: {0}", "Dernière vérification : {0}", "Última comprobación: {0}", "上次检查：{0}", "Última verificação: {0}", "Последняя проверка: {0}" },
        ["upd_never"]      = new[] { "never", "nigdy", "nie", "jamais", "nunca", "从未", "nunca", "никогда" },
        ["upd_available"]  = new[] { "New version {0} is available", "Dostępna nowa wersja {0}", "Neue Version {0} verfügbar", "Nouvelle version {0} disponible", "Nueva versión {0} disponible", "有新版本 {0}", "Nova versão {0} disponível", "Доступна новая версия {0}" },
        ["upd_download"]   = new[] { "Download", "Pobierz", "Herunterladen", "Télécharger", "Descargar", "下载", "Baixar", "Скачать" },
        ["upd_history"]    = new[] { "Release history", "Historia wydań", "Versionsverlauf", "Historique des versions", "Historial de versiones", "发布历史", "Histórico de versões", "История версий" },
        ["upd_details"]    = new[] { "Details", "Szczegóły", "Details", "Détails", "Detalles", "详情", "Detalhes", "Подробности" },
        ["upd_offline"]    = new[] { "Couldn't reach GitHub. Check your connection and try again.", "Nie udało się połączyć z GitHub. Sprawdź połączenie i spróbuj ponownie.", "GitHub nicht erreichbar. Verbindung prüfen und erneut versuchen.", "Impossible de joindre GitHub. Vérifiez la connexion et réessayez.", "No se pudo conectar con GitHub. Revisa la conexión e inténtalo de nuevo.", "无法连接 GitHub。请检查网络后重试。", "Não foi possível acessar o GitHub. Verifique a conexão e tente novamente.", "Не удалось подключиться к GitHub. Проверьте соединение и повторите." },
        ["set_advanced"]   = new[] { "Advanced settings (colours, hotkeys)…", "Ustawienia zaawansowane (kolory, skróty)…", "Erweiterte Einstellungen (Farben, Tastenkürzel)…", "Paramètres avancés (couleurs, raccourcis)…", "Ajustes avanzados (colores, atajos)…", "高级设置（颜色、快捷键）…", "Configurações avançadas (cores, atalhos)…", "Расширенные настройки (цвета, горячие клавиши)…" },
        ["set_theme"]      = new[] { "Theme", "Motyw", "Design", "Thème", "Tema", "主题", "Tema", "Тема" },
        ["set_theme_light"]= new[] { "Light", "Jasny", "Hell", "Clair", "Claro", "浅色", "Claro", "Светлая" },
        ["set_theme_dark"] = new[] { "Dark", "Ciemny", "Dunkel", "Sombre", "Oscuro", "深色", "Escuro", "Тёмная" },
        ["set_check_updates"] = new[] { "Check for updates automatically", "Sprawdzaj aktualizacje automatycznie", "Automatisch nach Updates suchen", "Vérifier les mises à jour automatiquement", "Buscar actualizaciones automáticamente", "自动检查更新", "Verificar atualizações automaticamente", "Автоматически проверять обновления" },
        ["menu_language"]  = new[] { "Language", "Język", "Sprache", "Langue", "Idioma", "语言", "Idioma", "Язык" },
        ["menu_exit"]      = new[] { "Exit", "Zamknij", "Beenden", "Quitter", "Salir", "退出", "Sair", "Выход" },

        ["set_hotkeys"]    = new[] { "Keyboard shortcuts", "Skróty klawiszowe", "Tastenkürzel", "Raccourcis clavier", "Atajos de teclado", "键盘快捷键", "Atalhos de teclado", "Горячие клавиши" },
        ["set_hint"]       = new[] { "Click a field and press a combo.  Esc / Delete = clear.", "Kliknij pole i wciśnij kombinację.  Esc / Delete = wyczyść.", "Feld anklicken und Kombination drücken.  Esc / Entf = löschen.", "Cliquez sur un champ et appuyez sur une combinaison.  Échap / Suppr = effacer.", "Haz clic en un campo y pulsa una combinación.  Esc / Supr = borrar.", "点击字段并按下组合键。Esc / Delete = 清除。", "Clique num campo e pressione uma combinação.  Esc / Delete = limpar.", "Нажмите поле и введите комбинацию.  Esc / Delete = очистить." },
        ["cycle"]          = new[] { "Cycle (next)", "Cykl (następny)", "Wechseln (nächstes)", "Cycle (suivant)", "Ciclo (siguiente)", "循环（下一个）", "Ciclo (próximo)", "Цикл (следующий)" },
        ["set_autostart"]  = new[] { "Start with Windows", "Uruchamiaj z Windowsem", "Mit Windows starten", "Démarrer avec Windows", "Iniciar con Windows", "随 Windows 启动", "Iniciar com o Windows", "Запускать с Windows" },
        ["set_default"]    = new[] { "Defaults", "Domyślne", "Standard", "Défaut", "Predeterminado", "默认", "Padrão", "По умолчанию" },
        ["set_save"]       = new[] { "Save", "Zapisz", "Speichern", "Enregistrer", "Guardar", "保存", "Salvar", "Сохранить" },
        ["set_close"]      = new[] { "Close", "Zamknij", "Schließen", "Fermer", "Cerrar", "关闭", "Fechar", "Закрыть" },
        ["set_saved"]      = new[] { "✓ Saved", "✓ Zapisano", "✓ Gespeichert", "✓ Enregistré", "✓ Guardado", "✓ 已保存", "✓ Salvo", "✓ Сохранено" },
        ["set_reset_hint"] = new[] { "Defaults restored (click Save).", "Przywrócono domyślne (kliknij Zapisz).", "Standard wiederhergestellt (Speichern).", "Valeurs par défaut (cliquez Enregistrer).", "Restaurado (haz clic en Guardar).", "已恢复默认（点击保存）。", "Padrões restaurados (clique em Salvar).", "Восстановлено (нажмите Сохранить)." },
        ["set_language"]   = new[] { "Language", "Język", "Sprache", "Langue", "Idioma", "语言", "Idioma", "Язык" },
        ["set_colors"]     = new[] { "Profile colors", "Kolory profili", "Profilfarben", "Couleurs des profils", "Colores de perfil", "配置文件颜色", "Cores dos perfis", "Цвета профилей" },
        ["set_charge"]     = new[] { "Battery charge limit", "Limit ładowania baterii", "Akkuladelimit", "Limite de charge batterie", "Límite de carga", "电池充电限制", "Limite de carga", "Лимит заряда батареи" },
        ["charge_dont"]    = new[] { "Don't change", "Nie zmieniaj", "Nicht ändern", "Ne pas changer", "No cambiar", "不更改", "Não alterar", "Не менять" },
        ["set_autoswitch"] = new[] { "Auto-switch AC / battery", "Auto-przełączanie zasilacz / bateria", "Auto-Wechsel Netz / Akku", "Bascule auto secteur / batterie", "Cambio auto CA / batería", "电源/电池自动切换", "Troca auto tomada / bateria", "Автопереключение сеть / батарея" },
        ["on_ac"]          = new[] { "On AC", "Na zasilaczu", "Am Netz", "Sur secteur", "Con CA", "接通电源", "Na tomada", "От сети" },
        ["on_battery"]     = new[] { "On battery", "Na baterii", "Im Akku", "Sur batterie", "Con batería", "使用电池", "Na bateria", "От батареи" },

        ["status_title"]   = new[] { "Status / Diagnostics", "Status / Diagnostyka", "Status / Diagnose", "Statut / Diagnostic", "Estado / Diagnóstico", "状态 / 诊断", "Status / Diagnóstico", "Состояние / Диагностика" },
        ["st_profile"]     = new[] { "Active profile", "Aktywny profil", "Aktives Profil", "Profil actif", "Perfil activo", "当前配置", "Perfil ativo", "Активный профиль" },
        ["st_cpu_temp"]    = new[] { "CPU temperature", "Temperatura CPU", "CPU-Temperatur", "Température CPU", "Temperatura CPU", "CPU 温度", "Temperatura da CPU", "Температура ЦП" },
        ["st_gpu_temp"]    = new[] { "GPU temperature", "Temperatura GPU", "GPU-Temperatur", "Température GPU", "Temperatura GPU", "GPU 温度", "Temperatura da GPU", "Температура ГП" },
        ["st_cpu_fan"]     = new[] { "CPU fan", "Wentylator CPU", "CPU-Lüfter", "Ventilateur CPU", "Ventilador CPU", "CPU 风扇", "Ventilador da CPU", "Вентилятор ЦП" },
        ["st_gpu_fan"]     = new[] { "GPU fan", "Wentylator GPU", "GPU-Lüfter", "Ventilateur GPU", "Ventilador GPU", "GPU 风扇", "Ventilador da GPU", "Вентилятор ГП" },
        ["st_charge"]      = new[] { "Charge limit", "Limit ładowania", "Ladelimit", "Limite de charge", "Límite de carga", "充电限制", "Limite de carga", "Лимит заряда" },
        ["st_firmware"]    = new[] { "EC firmware", "Firmware EC", "EC-Firmware", "Firmware EC", "Firmware EC", "EC 固件", "Firmware EC", "Прошивка EC" },
        ["st_switches"]    = new[] { "Switches (session)", "Przełączeń (sesja)", "Wechsel (Sitzung)", "Changements (session)", "Cambios (sesión)", "切换次数（本次）", "Trocas (sessão)", "Переключений (сессия)" },
        ["st_in_profile"]  = new[] { "Time in profile", "Czas w profilu", "Zeit im Profil", "Temps dans le profil", "Tiempo en perfil", "当前配置时长", "Tempo no perfil", "Время в профиле" },
        ["st_autostart"]   = new[] { "Autostart", "Autostart", "Autostart", "Démarrage auto", "Inicio automático", "自动启动", "Início automático", "Автозапуск" },
        ["st_app_ver"]     = new[] { "App version", "Wersja aplikacji", "App-Version", "Version de l'app", "Versión de la app", "应用版本", "Versão do app", "Версия приложения" },
        ["st_refresh"]     = new[] { "Refresh", "Odśwież", "Aktualisieren", "Actualiser", "Actualizar", "刷新", "Atualizar", "Обновить" },
        ["always_on_top"]  = new[] { "Always on top", "Zawsze na wierzchu", "Immer im Vordergrund", "Toujours au-dessus", "Siempre visible", "总在最前", "Sempre no topo", "Поверх всех окон" },
        ["st_model"]       = new[] { "Model", "Model", "Modell", "Modèle", "Modelo", "型号", "Modelo", "Модель" },
        ["unsupported_title"] = new[] { "Unsupported model", "Niewspierany model", "Nicht unterstütztes Modell", "Modèle non pris en charge", "Modelo no compatible", "不支持的型号", "Modelo não suportado", "Модель не поддерживается" },
        ["unsupported_sub"]   = new[] { "read-only — contribute on GitHub", "tylko odczyt — zgłoś model na GitHub", "schreibgeschützt — auf GitHub beitragen", "lecture seule — contribuez sur GitHub", "solo lectura — contribuye en GitHub", "只读 — 在 GitHub 上贡献", "somente leitura — contribua no GitHub", "только чтение — добавьте на GitHub" },
        ["experimental_enable"] = new[] { "Enable experimental models (unverified)", "Włącz modele eksperymentalne (niezweryfikowane)", "Experimentelle Modelle aktivieren (ungeprüft)", "Activer les modèles expérimentaux (non vérifiés)", "Activar modelos experimentales (no verificados)", "启用实验性型号（未验证）", "Ativar modelos experimentais (não verificados)", "Включить экспериментальные модели (непроверенные)" },
        ["set_check_updates"] = new[] {
            "Check for updates (once a day)", "Sprawdzaj aktualizacje (raz dziennie)", "Auf Updates prüfen (täglich)",
            "Vérifier les mises à jour (une fois par jour)", "Buscar actualizaciones (una vez al día)",
            "检查更新（每天一次）", "Procurar atualizações (uma vez por dia)", "Проверять обновления (раз в день)" },
        ["update_available"] = new[] {
            "Update available", "Dostępna aktualizacja", "Update verfügbar", "Mise à jour disponible",
            "Actualización disponible", "有可用更新", "Atualização disponível", "Доступно обновление" },
        ["update_available_text"] = new[] {
            "Version {0} is available — click to download.", "Dostępna jest wersja {0} — kliknij, aby pobrać.",
            "Version {0} ist verfügbar – zum Herunterladen klicken.", "La version {0} est disponible — cliquez pour télécharger.",
            "La versión {0} está disponible — haz clic para descargar.", "新版本 {0} 可用 — 点击下载。",
            "A versão {0} está disponível — clique para baixar.", "Доступна версия {0} — нажмите, чтобы скачать." },
        ["menu_update"] = new[] {
            "⬇ Download new version", "⬇ Pobierz nową wersję", "⬇ Neue Version herunterladen",
            "⬇ Télécharger la nouvelle version", "⬇ Descargar nueva versión", "⬇ 下载新版本",
            "⬇ Baixar nova versão", "⬇ Скачать новую версию" },
        ["experimental_locked"] = new[] { "experimental — enable in Settings", "eksperymentalny — włącz w Ustawieniach", "experimentell — in Einstellungen aktivieren", "expérimental — activez dans Paramètres", "experimental — actívalo en Ajustes", "实验性 — 在设置中启用", "experimental — ative nas Configurações", "экспериментально — включите в настройках" },
        ["tier_experimental"]   = new[] { "experimental", "eksperymentalny", "experimentell", "expérimental", "experimental", "实验性", "experimental", "экспериментальный" },
        ["tier_tested"]         = new[] { "tested", "zweryfikowany", "getestet", "testé", "probado", "已测试", "testado", "проверено" },
        ["tier_unsupported"]    = new[] { "unsupported", "niewspierany", "nicht unterstützt", "non pris en charge", "no compatible", "不支持", "não suportado", "не поддерживается" },

        // ---- Report my model wizard ----
        ["menu_report"]    = new[] { "Report my model…", "Zgłoś mój model…", "Mein Modell melden…", "Signaler mon modèle…", "Reportar mi modelo…", "上报我的型号…", "Relatar meu modelo…", "Сообщить о модели…" },
        ["rep_title"]      = new[] { "Report my model", "Zgłoś mój model", "Mein Modell melden", "Signaler mon modèle", "Reportar mi modelo", "上报我的型号", "Relatar meu modelo", "Сообщить о модели" },
        ["rep_intro"]      = new[] {
            "Help add support for your laptop. This reads your EC in each MSI Center scenario (READ-ONLY — nothing is written) and prepares a GitHub report for you.",
            "Pomóż dodać wsparcie dla Twojego laptopa. Odczytamy EC w każdym scenariuszu MSI Center (TYLKO ODCZYT — nic nie jest zapisywane) i przygotujemy zgłoszenie na GitHub.",
            "Hilf, Unterstützung für dein Gerät hinzuzufügen. Liest den EC in jedem MSI-Center-Szenario (NUR LESEN — nichts wird geschrieben) und erstellt einen GitHub-Bericht.",
            "Aidez à prendre en charge votre PC. Lit l'EC dans chaque scénario MSI Center (LECTURE SEULE — rien n'est écrit) et prépare un rapport GitHub.",
            "Ayuda a añadir soporte para tu portátil. Lee el EC en cada escenario de MSI Center (SOLO LECTURA — no se escribe nada) y prepara un informe de GitHub.",
            "帮助为你的笔记本添加支持。将在每个 MSI Center 场景下读取 EC（只读——不写入任何内容）并为你准备 GitHub 报告。",
            "Ajude a adicionar suporte ao seu notebook. Lê o EC em cada cenário do MSI Center (SOMENTE LEITURA — nada é gravado) e prepara um relatório no GitHub.",
            "Помогите добавить поддержку вашего ноутбука. Считывает EC в каждом сценарии MSI Center (ТОЛЬКО ЧТЕНИЕ — ничего не записывается) и готовит отчёт на GitHub." },
        ["rep_need_msi"]   = new[] {
            "Requires MSI Center installed (to set each scenario as a reference).",
            "Wymaga zainstalowanego MSI Center (do ustawienia każdego scenariusza jako wzorca).",
            "Erfordert installiertes MSI Center (zum Setzen jedes Szenarios als Referenz).",
            "Nécessite MSI Center installé (pour définir chaque scénario comme référence).",
            "Requiere MSI Center instalado (para fijar cada escenario como referencia).",
            "需要已安装 MSI Center（用于将每个场景设为参考）。",
            "Requer o MSI Center instalado (para definir cada cenário como referência).",
            "Требуется установленный MSI Center (чтобы задать каждый сценарий как эталон)." },
        ["rep_msi_tip"]    = new[] {
            "Best with MSI Center 2.0.48 — the last version with a working SILENT scenario. Newer versions auto-update and silently drop SILENT after a reboot (exactly why this app exists).",
            "Najlepiej mieć MSI Center 2.0.48 — ostatnią wersję z działającym scenariuszem SILENT. Nowsze wersje same się aktualizują i po restarcie tracą tryb SILENT (właśnie dlatego powstała ta aplikacja).",
            "Am besten mit MSI Center 2.0.48 — der letzten Version mit funktionierendem SILENT-Szenario. Neuere Versionen aktualisieren sich selbst und verlieren SILENT nach einem Neustart (genau deshalb gibt es diese App).",
            "De préférence MSI Center 2.0.48 — la dernière version avec un scénario SILENT fonctionnel. Les versions plus récentes se mettent à jour seules et perdent SILENT après un redémarrage (la raison d'être de cette app).",
            "Mejor con MSI Center 2.0.48 — la última versión con el escenario SILENT funcional. Las versiones nuevas se autoactualizan y pierden SILENT tras reiniciar (justo por eso existe esta app).",
            "最好使用 MSI Center 2.0.48——最后一个 SILENT 场景可用的版本。较新版本会自动更新，重启后悄悄失去 SILENT（这正是本应用存在的原因）。",
            "Melhor com o MSI Center 2.0.48 — a última versão com o cenário SILENT funcionando. Versões mais novas se atualizam sozinhas e perdem o SILENT após reiniciar (exatamente por isso este app existe).",
            "Лучше всего MSI Center 2.0.48 — последняя версия с рабочим сценарием SILENT. Новые версии сами обновляются и теряют SILENT после перезагрузки (именно поэтому появилось это приложение)." },
        ["rep_msi_clean"]  = new[] {
            "Before installing 2.0.48, fully remove the current MSI Center with MSI's official cleaner:",
            "Przed instalacją 2.0.48 usuń całkowicie obecny MSI Center oficjalnym narzędziem MSI:",
            "Vor der Installation von 2.0.48 das aktuelle MSI Center mit dem offiziellen MSI-Cleaner vollständig entfernen:",
            "Avant d'installer 2.0.48, supprimez complètement le MSI Center actuel avec l'outil officiel MSI :",
            "Antes de instalar 2.0.48, elimina por completo el MSI Center actual con la herramienta oficial de MSI:",
            "安装 2.0.48 之前，请用 MSI 官方清理工具彻底卸载当前的 MSI Center：",
            "Antes de instalar o 2.0.48, remova completamente o MSI Center atual com a ferramenta oficial da MSI:",
            "Перед установкой 2.0.48 полностью удалите текущий MSI Center официальной утилитой MSI:" },
        ["rep_msi_download"] = new[] {
            "Get MSI Center 2.0.48 from Uptodown. Use the direct link; if it ever stops working, use the full version list as a fallback:",
            "Pobierz MSI Center 2.0.48 z Uptodown. Użyj linku bezpośredniego; gdyby przestał działać, skorzystaj z pełnej listy wersji jako zapasowej:",
            "MSI Center 2.0.48 von Uptodown laden. Direktlink verwenden; falls er nicht mehr funktioniert, die vollständige Versionsliste als Ausweichoption nutzen:",
            "Téléchargez MSI Center 2.0.48 depuis Uptodown. Utilisez le lien direct ; s'il cesse de fonctionner, utilisez la liste complète des versions en secours :",
            "Descarga MSI Center 2.0.48 desde Uptodown. Usa el enlace directo; si deja de funcionar, usa la lista completa de versiones como alternativa:",
            "从 Uptodown 获取 MSI Center 2.0.48。请使用直链；若失效，可改用完整版本列表作为备用：",
            "Baixe o MSI Center 2.0.48 no Uptodown. Use o link direto; se parar de funcionar, use a lista completa de versões como alternativa:",
            "Скачайте MSI Center 2.0.48 с Uptodown. Используйте прямую ссылку; если она перестанет работать, используйте полный список версий как запасной вариант:" },
        ["rep_dl_version"] = new[] {
            "Download MSI Center 2.0.48 (direct link)",
            "Pobierz MSI Center 2.0.48 (link bezpośredni)",
            "MSI Center 2.0.48 herunterladen (Direktlink)",
            "Télécharger MSI Center 2.0.48 (lien direct)",
            "Descargar MSI Center 2.0.48 (enlace directo)",
            "下载 MSI Center 2.0.48（直链）",
            "Baixar MSI Center 2.0.48 (link direto)",
            "Скачать MSI Center 2.0.48 (прямая ссылка)" },
        ["rep_dl_repo"] = new[] {
            "All MSI Center versions on Uptodown (fallback)",
            "Wszystkie wersje MSI Center na Uptodown (zapasowo)",
            "Alle MSI-Center-Versionen auf Uptodown (Ausweich)",
            "Toutes les versions de MSI Center sur Uptodown (secours)",
            "Todas las versiones de MSI Center en Uptodown (alternativa)",
            "Uptodown 上的所有 MSI Center 版本（备用）",
            "Todas as versões do MSI Center no Uptodown (alternativa)",
            "Все версии MSI Center на Uptodown (запасной вариант)" },
        ["rep_uninstaller_link"] = new[] {
            "Download CleanCenterMaster (official MSI uninstaller)",
            "Pobierz CleanCenterMaster (oficjalny deinstalator MSI)",
            "CleanCenterMaster herunterladen (offizieller MSI-Deinstaller)",
            "Télécharger CleanCenterMaster (désinstalleur officiel MSI)",
            "Descargar CleanCenterMaster (desinstalador oficial de MSI)",
            "下载 CleanCenterMaster（MSI 官方卸载工具）",
            "Baixar CleanCenterMaster (desinstalador oficial da MSI)",
            "Скачать CleanCenterMaster (официальный деинсталлятор MSI)" },
        ["rep_section"]    = new[] { "EC CAPTURE", "PRZECHWYTYWANIE EC", "EC-ERFASSUNG", "CAPTURE EC", "CAPTURA EC", "EC 采集", "CAPTURA DO EC", "СНЯТИЕ EC" },
        ["tab_report"]     = new[] { "Report model", "Zgłoś model", "Modell melden", "Signaler", "Reportar modelo", "提交机型", "Reportar modelo", "Сообщить" },
        ["set_grp_look"]   = new[] { "Appearance", "Wygląd", "Darstellung", "Apparence", "Apariencia", "外观", "Aparência", "Вид" },
        ["set_grp_power"]  = new[] { "Power", "Zasilanie", "Energie", "Alimentation", "Energía", "电源", "Energia", "Питание" },
        ["set_grp_start"]  = new[] { "Startup & tray", "Start i zasobnik", "Start & Infobereich", "Démarrage", "Inicio", "启动与托盘", "Inicialização", "Запуск" },
        ["set_grp_updates"]= new[] { "Updates", "Aktualizacje", "Updates", "Mises à jour", "Actualizaciones", "更新", "Atualizações", "Обновления" },
        ["rep_step"]       = new[] { "Step {0} of {1}", "Krok {0} z {1}", "Schritt {0} von {1}", "Étape {0} sur {1}", "Paso {0} de {1}", "第 {0} / {1} 步", "Etapa {0} de {1}", "Шаг {0} из {1}" },
        ["rep_set_scenario"] = new[] {
            "In MSI Center set the scenario to: {0}, then click Capture.",
            "W MSI Center ustaw scenariusz: {0}, następnie kliknij Przechwyć.",
            "Im MSI Center das Szenario auf {0} setzen, dann Erfassen klicken.",
            "Dans MSI Center, réglez le scénario sur : {0}, puis cliquez sur Capturer.",
            "En MSI Center fija el escenario en: {0}, luego pulsa Capturar.",
            "在 MSI Center 中将场景设为：{0}，然后点击采集。",
            "No MSI Center defina o cenário como: {0}, depois clique em Capturar.",
            "В MSI Center установите сценарий: {0}, затем нажмите «Снять»." },
        ["rep_capture"]    = new[] { "Capture", "Przechwyć", "Erfassen", "Capturer", "Capturar", "采集", "Capturar", "Снять" },
        ["rep_capturing"]  = new[] { "Reading EC…", "Odczyt EC…", "EC wird gelesen…", "Lecture de l'EC…", "Leyendo EC…", "正在读取 EC…", "Lendo EC…", "Чтение EC…" },
        ["rep_captured"]   = new[] { "captured", "przechwycono", "erfasst", "capturé", "capturado", "已采集", "capturado", "снято" },
        ["rep_pending"]    = new[] { "pending", "oczekuje", "ausstehend", "en attente", "pendiente", "待采集", "pendente", "ожидание" },
        ["rep_all_done"]   = new[] {
            "All scenarios captured. The report was copied to your clipboard and saved to a file. Click Finish to open the GitHub form — paste the full report (Ctrl+V) into the \"Full EC dump per scenario (optional, very helpful)\" field.",
            "Wszystkie scenariusze przechwycone. Raport skopiowano do schowka i zapisano do pliku. Kliknij Zakończ, aby otworzyć formularz GitHub — wklej pełny raport (Ctrl+V) w pole \"Full EC dump per scenario (optional, very helpful)\".",
            "Alle Szenarien erfasst. Der Bericht wurde in die Zwischenablage kopiert und als Datei gespeichert. Auf Fertig klicken, um das GitHub-Formular zu öffnen — vollständigen Bericht (Strg+V) in das Feld \"Full EC dump per scenario (optional, very helpful)\" einfügen.",
            "Tous les scénarios capturés. Le rapport a été copié dans le presse-papiers et enregistré. Cliquez sur Terminer pour ouvrir le formulaire GitHub — collez le rapport complet (Ctrl+V) dans le champ \"Full EC dump per scenario (optional, very helpful)\".",
            "Todos los escenarios capturados. El informe se copió al portapapeles y se guardó en un archivo. Pulsa Finalizar para abrir el formulario de GitHub — pega el informe completo (Ctrl+V) en el campo \"Full EC dump per scenario (optional, very helpful)\".",
            "已采集所有场景。报告已复制到剪贴板并保存为文件。点击完成以打开 GitHub 表单——将完整报告（Ctrl+V）粘贴到 \"Full EC dump per scenario (optional, very helpful)\" 字段。",
            "Todos os cenários capturados. O relatório foi copiado para a área de transferência e salvo em arquivo. Clique em Concluir para abrir o formulário do GitHub — cole o relatório completo (Ctrl+V) no campo \"Full EC dump per scenario (optional, very helpful)\".",
            "Все сценарии сняты. Отчёт скопирован в буфер обмена и сохранён в файл. Нажмите «Готово», чтобы открыть форму GitHub — вставьте полный отчёт (Ctrl+V) в поле \"Full EC dump per scenario (optional, very helpful)\"." },
        ["rep_finish"]     = new[] { "Finish & open GitHub", "Zakończ i otwórz GitHub", "Fertig & GitHub öffnen", "Terminer & ouvrir GitHub", "Finalizar y abrir GitHub", "完成并打开 GitHub", "Concluir e abrir GitHub", "Готово и открыть GitHub" },
        ["rep_cancel"]     = new[] { "Cancel", "Anuluj", "Abbrechen", "Annuler", "Cancelar", "取消", "Cancelar", "Отмена" },
        ["rep_saved_to"]   = new[] { "Saved to: {0}", "Zapisano do: {0}", "Gespeichert unter: {0}", "Enregistré dans : {0}", "Guardado en: {0}", "已保存到：{0}", "Salvo em: {0}", "Сохранено в: {0}" },
        ["rep_read_fail"]  = new[] {
            "Couldn't read the EC (is the MSI WMI interface available?). Details: {0}",
            "Nie udało się odczytać EC (czy interfejs WMI MSI jest dostępny?). Szczegóły: {0}",
            "EC konnte nicht gelesen werden (ist die MSI-WMI-Schnittstelle verfügbar?). Details: {0}",
            "Impossible de lire l'EC (l'interface WMI MSI est-elle disponible ?). Détails : {0}",
            "No se pudo leer el EC (¿está disponible la interfaz WMI de MSI?). Detalles: {0}",
            "无法读取 EC（MSI WMI 接口是否可用？）。详情：{0}",
            "Não foi possível ler o EC (a interface WMI da MSI está disponível?). Detalhes: {0}",
            "Не удалось прочитать EC (доступен ли интерфейс MSI WMI?). Подробности: {0}" },

        ["yes"]            = new[] { "Yes", "Tak", "Ja", "Oui", "Sí", "是", "Sim", "Да" },
        ["no"]             = new[] { "No", "Nie", "Nein", "Non", "No", "否", "Não", "Нет" },
        ["err"]            = new[] { "ERROR", "BŁĄD", "FEHLER", "ERREUR", "ERROR", "错误", "ERRO", "ОШИБКА" },

        ["sub_silent"]       = new[] { "quiet · ~30–40 W", "cicho · ~30–40 W", "leise · ~30–40 W", "silencieux · ~30–40 W", "silencioso · ~30–40 W", "安静 · ~30–40 W", "silencioso · ~30–40 W", "тихо · ~30–40 W" },
        ["sub_balanced"]     = new[] { "full power", "pełna moc", "volle Leistung", "pleine puissance", "máxima potencia", "全功率", "potência total", "полная мощность" },
        ["sub_extreme"]      = new[] { "max · loud", "maks · głośno", "max · laut", "max · bruyant", "máx · ruidoso", "最大 · 吵", "máx · ruidoso", "макс · громко" },
        ["sub_superbattery"] = new[] { "saving · ~15 W", "oszczędzanie · ~15 W", "sparen · ~15 W", "économie · ~15 W", "ahorro · ~15 W", "省电 · ~15 W", "economia · ~15 W", "экономия · ~15 W" },
    };
}
