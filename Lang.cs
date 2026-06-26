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
        ["experimental_locked"] = new[] { "experimental — enable in Settings", "eksperymentalny — włącz w Ustawieniach", "experimentell — in Einstellungen aktivieren", "expérimental — activez dans Paramètres", "experimental — actívalo en Ajustes", "实验性 — 在设置中启用", "experimental — ative nas Configurações", "экспериментально — включите в настройках" },
        ["tier_experimental"]   = new[] { "experimental", "eksperymentalny", "experimentell", "expérimental", "experimental", "实验性", "experimental", "экспериментальный" },

        ["yes"]            = new[] { "Yes", "Tak", "Ja", "Oui", "Sí", "是", "Sim", "Да" },
        ["no"]             = new[] { "No", "Nie", "Nein", "Non", "No", "否", "Não", "Нет" },
        ["err"]            = new[] { "ERROR", "BŁĄD", "FEHLER", "ERREUR", "ERROR", "错误", "ERRO", "ОШИБКА" },

        ["sub_silent"]       = new[] { "quiet · ~30–40 W", "cicho · ~30–40 W", "leise · ~30–40 W", "silencieux · ~30–40 W", "silencioso · ~30–40 W", "安静 · ~30–40 W", "silencioso · ~30–40 W", "тихо · ~30–40 W" },
        ["sub_balanced"]     = new[] { "full power", "pełna moc", "volle Leistung", "pleine puissance", "máxima potencia", "全功率", "potência total", "полная мощность" },
        ["sub_extreme"]      = new[] { "max · loud", "maks · głośno", "max · laut", "max · bruyant", "máx · ruidoso", "最大 · 吵", "máx · ruidoso", "макс · громко" },
        ["sub_superbattery"] = new[] { "saving · ~15 W", "oszczędzanie · ~15 W", "sparen · ~15 W", "économie · ~15 W", "ahorro · ~15 W", "省电 · ~15 W", "economia · ~15 W", "экономия · ~15 W" },
    };
}
