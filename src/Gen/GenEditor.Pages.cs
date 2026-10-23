// Страницы редактора файла ответов: тексты и поля повторяют вкладки оригинального генератора.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinInstaller
{
    partial class GenEditor
    {
        void BuildPages()
        {
            PagePresets(); PageLang(); PageInstall(); PageSystem(); PageUsers(); PageTweaks(); PageDesktop(); PageDefender();
            PageAccess(); PageActivation(); PageApps(); PagePrograms(); PageScripts(); PageTasks(); PageResult();
        }

        // ------------------------------------------------------------ быстрый старт

        readonly JObj quick = GenState.J("name", "User", "pass", "", "pc", "");

        void ApplyPreset(string id, string title)
        {
            if (MessageBox.Show(this, "Применить набор «" + title + "»?\n\nБудут заменены: твики, удаляемые приложения, Защитник, удалённый доступ и список программ. Остальные настройки останутся.",
                "Быстрый старт", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            GenPresets.Apply(id, St);
            LoadAll();
        }

        void PagePresets()
        {
            GPage p = AddPage("presets", "Быстрый старт", "star");
            GCard c0 = AddCard(p, "Windows 10 Pro без OOBE", null);
            RowCtx x = c0.Ctx;
            Note(x, "Одним щелчком настраивает файл ответов так, чтобы установка Windows 10 Pro прошла без экранов первого запуска (OOBE): без лицензионного соглашения, без входа в учётную запись Microsoft и без «быстрых параметров». Создаётся локальный пользователь. Диск вы по-прежнему выбираете сами в установщике, поэтому ничего не будет стёрто без вашего ведома.");
            Txt(x, BI(quick, "name"), "Имя пользователя", half: true);
            Txt(x, BI(quick, "pass"), "Пароль (пусто = без пароля)", half: true);
            Txt(x, BI(quick, "pc"), "Имя компьютера (пусто = случайное)", half: true);
            Buttons(x, new string[] { "Применить: Pro без OOBE" }, new Action[] { delegate
            {
                GenPresets.Oobe(St, quick.S("name").Trim(), quick.S("pass"), quick.S("pc").Trim());
                LoadAll();
                MessageBox.Show(this, "Готово. Проверьте остальные вкладки и откройте «Результат», чтобы сохранить файл или использовать его для установки.", "Windows 10 Pro без OOBE");
            } }, null, 220);

            GCard c1 = AddCard(p, "Готовые наборы настроек", null);
            x = c1.Ctx;
            Note(x, "Набор расставляет твики, удаляемые приложения, Защитник, удалённый доступ и список программ. Язык, пользователи, диск, сеть, активация и ваши скрипты не меняются — после применения всё можно поправить на других вкладках.");
            foreach (PresetInfo pi in Cat.Presets)
            {
                PresetInfo q = pi;
                Sub(x, q.Title);
                Note(x, q.Desc);
                Buttons(x, new string[] { "Применить" }, new Action[] { delegate { ApplyPreset(q.Id, q.Title); } });
            }

            GCard c2 = AddCard(p, "Файл настроек", null);
            Note(c2.Ctx, "Настройки сохраняются в JSON — того же вида, что у оригинального генератора, поэтому файлы совместимы. Внимание: в файле пароли лежат открытым текстом.");
            Buttons(c2.Ctx, new string[] { "Загрузить настройки…", "Сохранить настройки…", "Сбросить всё" }, new Action[] { LoadSettingsDialog, SaveSettingsDialog, ResetDialog }, null, 160);
        }

        // ------------------------------------------------------------ язык и регион

        GRow KbdRow(RowCtx x)
        {
            GRow r = NewRow(x, false, null);
            List<CheckBox> cbs = new List<CheckBox>();
            for (int i = 0; i < Cat.Kbds.Count; i++)
            {
                UiCheck cb = new UiCheck();
                cb.AutoSize = false;
                cb.Text = Cat.Kbds[i][1];
                r.Host.Controls.Add(cb);
                cbs.Add(cb);
                cb.CheckedChanged += delegate
                {
                    if (loading) return;
                    List<object> sel = new List<object>();
                    for (int k = 0; k < cbs.Count; k++) if (cbs[k].Checked) sel.Add(Cat.Kbds[k][0]);
                    St.O("locale")["kbd"] = sel;
                    Changed();
                };
            }
            r.Fit = delegate(int w)
            {
                int cols = Math.Max(1, Math.Min(3, w / 230)), colW = w / cols;
                for (int i = 0; i < cbs.Count; i++) cbs[i].SetBounds((i % cols) * colW, (i / cols) * 28, colW - 6, 24);
                return ((cbs.Count + cols - 1) / cols) * 28;
            };
            r.Load = delegate
            {
                List<object> kbd = St.O("locale").A("kbd") ?? new List<object>();
                for (int i = 0; i < cbs.Count; i++) { bool v = kbd.Contains(Cat.Kbds[i][0]); if (cbs[i].Checked != v) cbs[i].Checked = v; }
            };
            Reg("kbd", cbs[0]);
            return r;
        }

        void PageLang()
        {
            GPage p = AddPage("lang", "Язык и регион", "globe");
            GCard c = AddCard(p, "Язык и региональные параметры", null);
            RowCtx x = c.Ctx;
            Sel(x, B("locale.ui"), "Язык интерфейса и установщика", OptsOf(Cat.Langs), "Язык должен быть в вашем ISO. Если образ одноязычный (например, только English), выберите его язык, иначе установка остановится с ошибкой файла ответов.");
            Sel(x, B("locale.user"), "Региональные форматы (даты, числа)", OptsOf(Cat.Langs), half: true);
            Sel(x, B("locale.system"), "Язык программ без поддержки Юникода", OptsOf(Cat.Langs), half: true);
            Sub(x, "Раскладки клавиатуры");
            KbdRow(x);
            Sel(x, B("locale.kbdDefault"), "Раскладка по умолчанию", OptsOf(Cat.Kbds));
            GCard c2 = AddCard(p, "Часовой пояс", null);
            Sel(c2.Ctx, B("locale.tz"), "Часовой пояс", OptsOf(Cat.Timezones));
        }

        // ------------------------------------------------------------ установка / sysprep

        /// <summary>Пересобирает список выбора, если набор значений изменился, и подставляет допустимое значение в состояние.</summary>
        void FillCombo(GRow r, List<Opt> opts, Bind b, string fallback)
        {
            ComboBox cb = r.Combo;
            bool same = cb.Items.Count == opts.Count;
            for (int i = 0; same && i < opts.Count; i++) same = ((Opt)cb.Items[i]).Value == opts[i].Value;
            bool prev = loading;
            loading = true;
            try
            {
                if (!same) { cb.Items.Clear(); foreach (Opt o in opts) cb.Items.Add(o); }
                string cur = b.T().S(b.Key);
                if (!opts.Exists(delegate(Opt o) { return o.Value == cur; })) b.T()[b.Key] = fallback;
                r.Load();
            }
            finally { loading = prev; }
        }

        void PageInstall()
        {
            GPage p = AddPage("install", "Установка / Sysprep", "disc");
            Func<bool> clean = delegate() { return IsClean; }, sysp = delegate() { return IsSysprep; };
            Func<string> em = delegate() { return St.O("install").S("editionMode"); };
            Func<string> dm = delegate() { return St.O("install").S("diskMode"); };
            Func<bool> auto = delegate() { return dm() != "manual"; };
            Func<bool> winPos = delegate() { return St.O("install").I("winSize") > 0; };
            Func<bool> dataHere = delegate() { return St.O("install").B("dataPart") && winPos(); };

            GCard c1 = AddCard(p, "Что создаём", null);
            RowCtx x = c1.Ctx;
            Sel(x, B("os"), "Версия Windows", Opts("10", "Windows 10", "11", "Windows 11"), "От версии зависят список редакций, доступные твики и удаляемые приложения.", half: true);
            Sel(x, B("mode"), "Тип файла", Opts("clean", "Чистая установка — autounattend.xml", "sysprep", "Образ с Sysprep — unattend.xml"), half: true);
            Note(x, "Этот файл устанавливает Windows с нуля: разметка диска, выбор редакции, копирование файлов — всё делает установщик, читая этот файл с флешки.", clean);
            Note(x, "Этот файл не устанавливает Windows — он только донастраивает уже готовый образ при первом запуске после sysprep /generalize /oobe. Разметка диска и выбор редакции тут не нужны. Файл кладётся как %WINDIR%\\System32\\Sysprep\\unattend.xml.", sysp);

            GCard c2 = AddCard(p, "Редакция Windows", clean);
            x = c2.Ctx;
            Sel(x, B("arch"), "Архитектура", Opts("amd64", "x64 (64-бит)", "x86", "x86 (32-бит, только Windows 10)", "arm64", "ARM64"), half: true);
            Sel(x, B("install.editionMode"), "Как выбрать редакцию", Opts("generic", "Выбрать редакцию (универсальный ключ)", "custom", "Свой ключ продукта", "index", "Образ по номеру в install.wim", "ask", "Спросить при установке"), half: true);
            Bind be = B("install.edition");
            GRow edRow = Sel(x, be, "Редакция", new List<Opt>(), "Универсальный ключ только выбирает редакцию и не активирует Windows. Активация — потом, вашим ключом, цифровой лицензией или KMS. LTSC / IoT Enterprise: в таком ISO обычно одна редакция — можно выбрать здесь нужную или режим «Образ по номеру» (номер 1).",
                vis: delegate() { return em() == "generic"; });
            refreshers.Add(delegate
            {
                List<Opt> eds = new List<Opt>();
                foreach (Ed e in Cat.Editions) if (Cat.Has(e.Os, Os)) eds.Add(new Opt(e.Name, "Windows " + Os + " " + e.Name));
                FillCombo(edRow, eds, be, "Pro");
            });
            Txt(x, B("install.key"), "Ключ продукта", ph: "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", vis: delegate() { return em() == "custom"; });
            Num(x, B("install.index"), "Номер образа (1, 2, …)", 1, 99, "Посмотреть: dism /get-wiminfo /wimfile:D:\\sources\\install.wim", vis: delegate() { return em() == "index"; });
            Func<bool> w11 = delegate() { return Os == "11"; };
            Sub(x, "Windows 11: обход проверок оборудования", w11);
            Chk(x, B("install.bypass.tpm"), "Не проверять наличие TPM 2.0", vis: w11);
            Chk(x, B("install.bypass.sb"), "Не проверять Secure Boot", vis: w11);
            Chk(x, B("install.bypass.ram"), "Не проверять объём оперативной памяти (4 ГБ)", vis: w11);
            Chk(x, B("install.bypass.cpu"), "Не проверять поддерживаемый процессор", vis: w11);
            Chk(x, B("install.bypass.storage"), "Не проверять размер диска (64 ГБ)", vis: w11);
            Note(x, "Microsoft не поддерживает такие системы: часть обновлений может не приходить. Для обычного ПК обход не нужен.", w11);

            GCard c3 = AddCard(p, "Разметка диска", clean);
            x = c3.Ctx;
            Sel(x, B("install.diskMode"), "Режим", Opts("uefi", "Автоматически: очистить диск, UEFI / GPT", "bios", "Автоматически: очистить диск, BIOS / MBR (не для Windows 11)", "manual", "Вручную (диск выбирается в установщике)"));
            Alert(x, "⚠ Выбранный диск будет полностью очищен, без каких-либо подтверждений. Проверьте номер диска!", auto);
            Num(x, B("install.diskId"), "Номер диска", 0, 99, "Обычно 0. Если в ПК несколько дисков, сверьтесь с diskpart (list disk).", true, auto);
            Num(x, B("install.efiSize"), "Размер раздела EFI, МБ", 50, 100000, null, true, delegate() { return auto() && dm() == "uefi"; });
            Num(x, B("install.winSize"), "Размер раздела Windows, МБ", 0, 100000000, "0 — весь диск. Например, 100000 ≈ 100 ГБ.", true, auto);
            Chk(x, B("install.recovery"), "Создать раздел восстановления (WinRE)", vis: auto);
            Num(x, B("install.recoverySize"), "Размер раздела восстановления, МБ", 100, 1000000, null, false, delegate() { return auto() && St.O("install").B("recovery"); });
            Sub(x, "Диски для данных", auto);
            Chk(x, B("install.dataPart"), "Раздел с данными на этом же диске (из оставшегося места)", vis: delegate() { return auto() && winPos(); });
            Chk(x, B("install.dataDisk"), "Второй диск целиком под данные", "Второй диск тоже будет полностью очищен.", vis: auto);
            Func<bool> anyData = delegate() { return auto() && (dataHere() || St.O("install").B("dataDisk")); };
            Txt(x, B("install.dataLabel"), "Метка тома данных", ph: "Data", half: true, vis: anyData);
            Txt(x, B("install.dataLetter"), "Буква раздела данных", "Одна буква, кроме C. Если она занята, Windows назначит другую.", true, delegate() { return auto() && dataHere(); }, "D");
            Num(x, B("install.dataDiskId"), "Номер второго диска", 0, 99, null, true, delegate() { return auto() && St.O("install").B("dataDisk"); });
            Txt(x, B("install.dataDiskLetter"), "Буква второго диска", half: true, vis: delegate() { return auto() && St.O("install").B("dataDisk"); }, ph: "E");

            GCard c4 = AddCard(p, "Профиль и повторная активация", sysp);
            x = c4.Ctx;
            Chk(x, B("sysprep.copyProfile"), "Перенести настройки рабочего стола администратора, из-под которого собирали образ, всем пользователям (CopyProfile)", "Обычно выключено. Если включить — все личные файлы и настройки этого профиля станут профилем «по умолчанию» для новых пользователей.");
            Chk(x, B("sysprep.skipRearm"), "Не расходовать лимит переустановок активации при этом прогоне Sysprep (SkipRearm)", "Рекомендуется держать включённым — у Windows ограниченное число раз, когда можно «обнулить» активацию через generalize.");

            GCard c5 = AddCard(p, "DNS для сетевого адаптера", sysp);
            x = c5.Ctx;
            Note(x, "Применяется на этапе specialize, до входа в систему — полезно, если компьютер должен сразу резолвить внутренние имена (например, для добавления в домен).");
            Chk(x, B("sysprep.dnsOn"), "Задать DNS-серверы адаптера");
            Func<bool> dnsOn = delegate() { return St.O("sysprep").B("dnsOn"); };
            Txt(x, B("sysprep.dnsIface"), "Имя адаптера", "Как в Диспетчере устройств / Get-NetAdapter на эталонной машине", true, dnsOn, "Ethernet");
            Txt(x, B("sysprep.dns1"), "DNS 1", half: true, vis: dnsOn, ph: "192.168.1.1");
            Txt(x, B("sysprep.dns2"), "DNS 2", half: true, vis: dnsOn, ph: "8.8.8.8");

            GCard c6 = AddCard(p, "Имя компьютера при массовом развёртывании", sysp);
            x = c6.Ctx;
            Note(x, "150 компьютеров из одного образа получат одно и то же имя, если его не задать индивидуально. Скрипт переименовывает компьютер при первом входе и требует последующей перезагрузки.");
            Sel(x, B("sysprep.rename"), "Переименовывать компьютер", Opts("none", "Не переименовывать (оставить случайное имя от Windows)", "serial", "По серийному номеру из BIOS", "mac", "По MAC-адресу сетевой карты"));
            Txt(x, B("sysprep.renamePrefix"), "Префикс имени", "Итоговое имя: префикс + серийный номер/MAC, обрезано до 15 знаков", false, delegate() { return St.O("sysprep").S("rename") != "none"; }, "PC");

            GCard c7 = AddCard(p, "Стартовый скрипт Sysprep (Start-Sysprep.cmd)", sysp);
            x = c7.Ctx;
            Note(x, "Скрипт запускается на эталонной машине от администратора: проверяет наличие файлов, очищает систему, просит подтверждение и запускает sysprep /generalize /oobe с вашим unattend.xml. Сохраняется на странице «Результат».");
            Chk(x, B("sysprep.cleanup"), "Перед Sysprep очистить компоненты (DISM) и временные файлы");
            Chk(x, B("sysprep.clearEvt"), "Очистить журналы событий и старые логи Panther");
            Sel(x, B("sysprep.after"), "После успешного Sysprep", Opts("shutdown", "Выключить компьютер (рекомендуется — сразу снимать образ)", "reboot", "Перезагрузить", "quit", "Ничего не делать"));
            Txt(x, B("sysprep.services"), "Службы, которые нужно остановить и перевести на ручной запуск (имена, по одному в строке)", lines: 3);
            Txt(x, B("sysprep.preScript"), "Свой скрипт, который выполнить перед Sysprep (необязательно)", ph: "C:\\Tools\\before-sysprep.cmd");
        }

        // ------------------------------------------------------------ система

        void PageSystem()
        {
            GPage p = AddPage("system", "Система", "monitor");
            GCard c1 = AddCard(p, "Компьютер", null);
            RowCtx x = c1.Ctx;
            Txt(x, B("sys.name"), "Имя компьютера", "До 15 знаков: латиница, цифры, дефис. Пусто = случайное", true);
            Txt(x, B("sys.owner"), "Владелец", half: true);
            Txt(x, B("sys.org"), "Организация", half: true);
            Sel(x, B("sys.netMode"), "Участие в сети", Opts("workgroup", "Рабочая группа", "domain", "Домен Active Directory"), half: true);
            Func<bool> dom = delegate() { return St.O("sys").S("netMode") == "domain"; };
            Txt(x, B("sys.workgroup"), "Рабочая группа", vis: delegate() { return !dom(); });
            Txt(x, B("sys.domain"), "Домен", half: true, vis: dom, ph: "corp.example.com");
            Txt(x, B("sys.domUser"), "Пользователь с правом ввода в домен", half: true, vis: dom);
            Txt(x, B("sys.domPass"), "Пароль", half: true, vis: dom);
            Txt(x, B("sys.domOU"), "OU (необязательно)", half: true, vis: dom, ph: "OU=PC,DC=corp,DC=example,DC=com");

            GCard c2 = AddCard(p, "Первый запуск (OOBE)", null);
            x = c2.Ctx;
            Chk(x, B("sys.hideEula"), "Не показывать лицензионное соглашение");
            Chk(x, B("sys.hideOEM"), "Не показывать экран регистрации OEM");
            Chk(x, B("sys.hideOnline"), "Пропустить вход через учётную запись Microsoft (использовать локальную)");
            Chk(x, B("sys.hideWireless"), "Не показывать экран настройки Wi-Fi");
            Chk(x, B("sys.express"), "Пропустить «Быстрые параметры» (приватность — по умолчанию отключена)");
            Sel(x, B("sys.netLoc"), "Сетевое расположение", Opts("Work", "Рабочая сеть", "Home", "Домашняя сеть", "Other", "Общедоступная сеть", "", "Спросить"), half: true);
            Chk(x, B("sys.reboot"), "Перезагрузить компьютер после выполнения всех команд при первом входе");

            GCard c3 = AddCard(p, "Wi-Fi (профиль подключается при первом входе)", null);
            x = c3.Ctx;
            Chk(x, B("sys.wifiOn"), "Добавить сеть Wi-Fi");
            Func<bool> wifi = delegate() { return St.O("sys").B("wifiOn"); };
            Txt(x, B("sys.ssid"), "Имя сети (SSID)", half: true, vis: wifi);
            Sel(x, B("sys.wifiAuth"), "Защита", Opts("WPA2PSK", "WPA2-Personal (AES)", "open", "Открытая сеть"), half: true, vis: wifi);
            Txt(x, B("sys.wifiPass"), "Пароль", half: true, vis: delegate() { return wifi() && St.O("sys").S("wifiAuth") != "open"; });
        }

        // ------------------------------------------------------------ пользователи

        void PageUsers()
        {
            GPage p = AddPage("users", "Пользователи", "user");
            GCard c1 = AddCard(p, "Учётные записи (локальные)", null);
            ListEditor(c1, "users", "+ Добавить пользователя", "Нет пользователей.");

            GCard c2 = AddCard(p, "Автоматический вход", null);
            RowCtx x = c2.Ctx;
            Chk(x, B("autologon.on"), "Входить автоматически после установки");
            Func<bool> on = delegate() { return St.O("autologon").B("on"); };
            GRow au = AutoUserRow(x, on);
            Num(x, B("autologon.count"), "Сколько раз входить автоматически", 1, 999999999, "Число входов (перезагрузок) без ввода пароля. После этого Windows спросит пароль.", true, delegate() { return on() && !St.O("autologon").B("always"); });
            Chk(x, B("autologon.always"), "Входить автоматически всегда (без ограничения числа перезагрузок)", vis: on);

            GCard c3 = AddCard(p, "Встроенный «Администратор»", null);
            x = c3.Ctx;
            Chk(x, B("admin.enable"), "Включить встроенную учётную запись администратора");
            Txt(x, B("admin.pass"), "Пароль встроенного администратора", "Если пусто — пароль не меняется");
            Chk(x, B("admin.hide"), "Скрыть встроенного администратора с экрана входа", "Вход возможен вводом имени вручную (.\\Администратор)");

            GCard c4 = AddCard(p, "Хранение паролей в файле", null);
            Chk(c4.Ctx, B("obfuscate"), "Кодировать пароли в Base64", "Это не шифрование — лишь защита от случайного взгляда. Файл всё равно нужно держать в тайне.");
        }

        /// <summary>Выбор пользователя для автовхода: «встроенный администратор» (-1) и пользователи из списка.</summary>
        GRow AutoUserRow(RowCtx x, Func<bool> vis)
        {
            GRow r = NewRow(x, true, vis);
            Label l = MakeLabel("Пользователь", Font, Theme.Text, 0, 0, 100);
            UiCombo cb = new UiCombo();
            r.Host.Controls.Add(l); r.Host.Controls.Add(cb);
            r.Fit = delegate(int w)
            {
                Wrap(l, w);
                cb.SetBounds(0, l.Bottom + 5, w, cb.Height);
                return cb.Bottom;
            };
            r.Combo = cb;
            Action fill = delegate
            {
                List<string> names = new List<string>();
                names.Add("Встроенный «Администратор»");
                List<JObj> us = St.Objs("users");
                for (int i = 0; i < us.Count; i++) names.Add(us[i].S("name").Length > 0 ? us[i].S("name") : "Пользователь " + (i + 1));
                bool same = cb.Items.Count == names.Count;
                for (int i = 0; same && i < names.Count; i++) same = (string)cb.Items[i] == names[i];
                if (!same) { cb.Items.Clear(); foreach (string n in names) cb.Items.Add(n); }
                JObj al = St.O("autologon");
                if (al.I("user") >= us.Count) al["user"] = 0;
                int idx = al.I("user") + 1;
                if (idx >= 0 && idx < cb.Items.Count && cb.SelectedIndex != idx) cb.SelectedIndex = idx;
            };
            r.Load = fill;
            refreshers.Add(delegate { bool prev = loading; loading = true; try { fill(); } finally { loading = prev; } });
            cb.SelectedIndexChanged += delegate
            {
                if (loading || cb.SelectedIndex < 0) return;
                St.O("autologon")["user"] = cb.SelectedIndex - 1;
                Changed();
            };
            Reg("autologon.user", cb);
            return r;
        }

        // ------------------------------------------------------------ настройки Windows

        string twSearch = "", twMode = "all";

        void ApplyTweakFilter()
        {
            string q = twSearch.Trim().ToLowerInvariant();
            foreach (GRow r in allRows)
            {
                Tw it = r.Tag as Tw;
                if (it == null) continue;
                bool ok = (q.Length == 0 || r.Search.ToLowerInvariant().Contains(q)) &&
                    (twMode == "all" || (twMode == "rec" && it.Rec) || (twMode == "risk" && it.Warn.Length > 0) ||
                     (twMode == "on" && St.O("tweaks").B(it.Id)));
                r.Filtered = !ok;
            }
            RelayoutAll();
        }

        void PageTweaks()
        {
            GPage p = AddPage("tweaks", "Настройки Windows", "gear");
            GCard bar = AddCard(p, "Поиск", null);
            GRow sr = NewRow(bar.Ctx, false, null);
            TextBox tb = new TextBox();
            FieldBox tbBox = FieldBox.Wrap(tb);
            tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, new IntPtr(1), "Поиск по настройкам…"); };
            UiCombo fc = new UiCombo();
            fc.Items.Add(new Opt("all", "Все")); fc.Items.Add(new Opt("rec", "Рекомендуемые")); fc.Items.Add(new Opt("risk", "Рискованные")); fc.Items.Add(new Opt("on", "Только включённые"));
            fc.SelectedIndex = 0;
            sr.Host.Controls.Add(tbBox); sr.Host.Controls.Add(fc);
            sr.Fit = delegate(int w)
            {
                int fw = Math.Min(230, w / 3);
                tbBox.SetBounds(0, 0, w - fw - 10, 32);
                fc.SetBounds(w - fw, 0, fw, fc.Height);
                return 34;
            };
            tb.TextChanged += delegate { twSearch = tb.Text; ApplyTweakFilter(); };
            fc.SelectedIndexChanged += delegate { twMode = ((Opt)fc.SelectedItem).Value; ApplyTweakFilter(); };

            foreach (TwGroup g in Cat.Tweaks)
            {
                TwGroup grp = g;
                GCard c = AddCard(p, grp.Name, null);
                RowCtx x = c.Ctx;
                Buttons(x, new string[] { "Все", "Сбросить" }, new Action[] {
                    delegate { foreach (Tw it in grp.Items) if (Cat.OsOk(it, Os)) St.O("tweaks")[it.Id] = true; LoadAll(); },
                    delegate { foreach (Tw it in grp.Items) St.O("tweaks")[it.Id] = false; LoadAll(); } }, null, 90);
                foreach (Tw t in grp.Items)
                {
                    Tw it = t;
                    string tags = (it.Warn.Length > 0 ? "   [рискованно]" : it.Rec ? "   [рекомендуется]" : "") + (it.Os.Length > 0 ? "   [только Windows " + it.Os + "]" : "");
                    GRow r = Chk(x, BMap("tweaks", it.Id, "tw:" + it.Id), it.Label, it.Hint.Length > 0 ? it.Hint : null, false, delegate() { return Cat.OsOk(it, Os); }, it.Warn.Length > 0 ? it.Warn : null, tags);
                    r.Tag = it;
                }
            }
            GCard cf = AddCard(p, "Компоненты Windows (включаются при первом входе)", null);
            foreach (string[] f in Cat.Features)
            {
                string[] ft = f;
                Chk(cf.Ctx, BMap("tweaks", "ft_" + ft[0], null), ft[1], null, false, null, ft[0] == "SMB1Protocol" ? "Небезопасно." : null);
            }
            GCard cr = AddCard(p, "Свои записи реестра (HKLM\\… или HKCU\\…; HKCU применяется ко всем новым пользователям)", null);
            ListEditor(cr, "regs", "+ Добавить параметр", "");
        }

        // ------------------------------------------------------------ защитник

        void PageDefender()
        {
            GPage p = AddPage("defender", "Защитник и безопасность", "shield");
            GCard c1 = AddCard(p, "Защитник Windows (Microsoft Defender)", null);
            RowCtx x = c1.Ctx;
            Sel(x, B("def.mode"), "Режим", Opts("keep", "Оставить включённым", "off", "Отключить (политики: защита в реальном времени, облако, уведомления)", "hard", "Отключить полностью (политики + отключение служб и драйверов)"));
            Alert(x, "⚠ Установщик применяет политики и (при первом входе) команду Set-MpPreference. Если включена «Защита от подделки» (Tamper Protection), Windows может отклонить эти изменения — это защита самой Windows, обойти её файлом ответов нельзя. Режим «полностью» дополнительно пытается отключить службы WinDefend, WdFilter и др.; часть этих ключей защищена системой. Удалить Защитник из Windows как компонент нельзя. Самый надёжный вариант — исключения (ниже) или отключение Tamper Protection вручную. Результат обязательно проверьте на виртуальной машине.",
                delegate() { return St.O("def").S("mode") != "keep"; });
            GCard c2 = AddCard(p, "Исключения (применяются независимо от режима)", null);
            x = c2.Ctx;
            Txt(x, B("def.exclPaths"), "Папки и файлы (по одному в строке)", lines: 3);
            Txt(x, B("def.exclExt"), "Расширения (по одному в строке, без точки)", lines: 2);
            Txt(x, B("def.exclProc"), "Процессы (по одному в строке)", lines: 2);
            GCard c3 = AddCard(p, "Права на папки (ACL)", null);
            x = c3.Ctx;
            Note(x, "Папки, из которых что-то запускается от имени SYSTEM или администратора (например, через планировщик или автозапуск), не должны быть доступны на запись обычным пользователям — иначе любой пользователь может подменить файл и получить те же права. Эта настройка ограничивает права на указанные папки: запись только у администраторов и SYSTEM, у остальных — только чтение и выполнение.");
            Txt(x, B("aclPaths"), "Папки (по одной в строке, должны уже существовать в образе к этому моменту)", lines: 3);

            GCard c4 = AddCard(p, "Правила брандмауэра Windows (разрешающие)", null);
            x = c4.Ctx;
            Note(x, "Для программы создаются два правила (входящее и исходящее) без ограничения портов и адресов. Правила для папок строятся при первом входе по найденным там .exe, поэтому программа должна уже лежать в образе. Порты — отдельные правила. Все правила попадают в группу «Buba-Helper».");
            Txt(x, B("fw.progs"), "Программы: полные пути к .exe (по одному в строке)", lines: 3, ph: "C:\\Program Files\\Microsoft SQL Server\\MSSQL14.MSSQLSERVER\\MSSQL\\Binn\\sqlservr.exe");
            Txt(x, B("fw.dirs"), "Папки: правила для всех .exe внутри (по одной в строке)", lines: 3, ph: "C:\\Program Files (x86)\\MyApp");
            Txt(x, B("fw.tcpIn"), "Входящие порты TCP (через запятую или пробел, диапазон 4890-4899)", half: true, ph: "1433, 4890-4899");
            Txt(x, B("fw.udpIn"), "Входящие порты UDP", half: true, ph: "1434");
            Txt(x, B("fw.tcpOut"), "Исходящие порты TCP (например, порт сервера)", half: true, ph: "2303");
            Chk(x, B("fw.defender"), "Добавить найденные программы в исключения процессов Защитника");
        }

        // ------------------------------------------------------------ рабочий стол и ярлыки

        void PageDesktop()
        {
            GPage p = AddPage("desktop", "Рабочий стол и ярлыки", "layout");
            GCard c1 = AddCard(p, "Обои рабочего стола", null);
            RowCtx x = c1.Ctx;
            Func<bool> on = delegate() { return St.O("wall").B("on"); };
            Chk(x, B("wall.on"), "Задать обои из файла");
            Txt(x, B("wall.path"), "Полный путь к картинке", "Файл должен лежать в образе до первого входа, например в C:\\Script. Картинку можно хранить с любым расширением.", false, on, "C:\\Script\\logo.jpg");
            Sel(x, B("wall.style"), "Положение", Opts("center", "По центру", "tile", "Мозаика", "stretch", "Растянуть", "fit", "По размеру", "fill", "Заполнение", "span", "Расширить на все мониторы"), half: true, vis: on);
            Chk(x, B("wall.lock"), "Запретить пользователям менять обои", vis: on);
            Chk(x, B("wall.black"), "Чёрный фон вокруг картинки (для режимов «По размеру» и «По центру»)", vis: on);
            Note(x, "Windows читает политику обоев из профиля пользователя, а не из настроек компьютера, поэтому значения записываются в шаблон Default (для новых пользователей) и при первом входе каждого пользователя (Active Setup, без окна).", on);
            GCard c2 = AddCard(p, "Ярлыки: переименование и копирование на рабочий стол", null);
            x = c2.Ctx;
            Note(x, "Выполняется при первом входе. Нужно, например, чтобы переименовать ярлыки установленной программы в меню «Пуск» и вынести главные из них на рабочий стол всех пользователей. Ярлыки должны уже лежать в образе.");
            Txt(x, B("lnk.dir"), "Папка с ярлыками", ph: "C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\MyApp");
            Txt(x, B("lnk.map"), "Переименовать (по одной паре в строке: Старое имя = Новое имя; с расширением .lnk для ярлыков; папки — без расширения)", lines: 4, ph: "Касса.lnk = Каса.lnk");
            Txt(x, B("lnk.desktop"), "Скопировать на рабочий стол всех пользователей (имена файлов после переименования, по одному в строке)", lines: 3, ph: "Каса.lnk");
            GCard c3 = AddCard(p, "Что ещё настраивается здесь", null);
            Note(c3.Ctx, "На вкладке «Настройки Windows», группа «Образ и рабочее место»: запрет смены темы, значок «Провести собрание», значки «Этот компьютер» и «Панель управления» только у встроенного Администратора, скрытие папок профиля в «Этом компьютере», закрытие Центра обновления для всех, кроме Администратора. Там же — скрытие поля поиска (теперь устойчиво к повторному включению Windows).");
        }

        // ------------------------------------------------------------ сеть и доступ

        void PageAccess()
        {
            GPage p = AddPage("access", "Сеть и доступ", "net");
            GCard c1 = AddCard(p, "Удалённый рабочий стол (RDP)", null);
            RowCtx x = c1.Ctx;
            Func<bool> rdp = delegate() { return St.O("remote").B("rdp"); };
            Chk(x, B("remote.rdp"), "Включить удалённый рабочий стол и открыть порт в брандмауэре");
            Num(x, B("remote.rdpPort"), "Порт RDP", 1, 65535, "Стандартный — 3389. Смена порта: подключаться как host:порт", true, rdp);
            Chk(x, B("remote.nla"), "Требовать проверку подлинности на уровне сети (NLA)", "Безопаснее. Отключите, если подключаетесь старыми клиентами. Для NLA у пользователя должен быть пароль.", vis: rdp);
            GCard c2 = AddCard(p, "Прочий удалённый доступ", null);
            x = c2.Ctx;
            Chk(x, B("remote.winrm"), "Включить WinRM / PowerShell Remoting (Enable-PSRemoting)", "Также разрешает удалённое администрирование локальными админами (LocalAccountTokenFilterPolicy)");
            Chk(x, B("remote.ping"), "Разрешить ответ на ping (ICMP)");
            Chk(x, B("remote.fps"), "Включить общий доступ к файлам и принтерам");
            Chk(x, B("remote.discovery"), "Включить сетевое обнаружение");
            GCard c3 = AddCard(p, "Сетевой адаптер (применяется при первом входе)", null);
            x = c3.Ctx;
            Func<bool> st = delegate() { return St.O("net").B("static"); };
            Chk(x, B("net.static"), "Задать статический IPv4-адрес первому физическому адаптеру");
            Txt(x, B("net.ip"), "IP-адрес", half: true, vis: st, ph: "192.168.1.50");
            Num(x, B("net.prefix"), "Длина маски (24 = 255.255.255.0)", 1, 32, null, true, st);
            Txt(x, B("net.gw"), "Шлюз", half: true, vis: st, ph: "192.168.1.1");
            Txt(x, B("net.dns1"), "DNS 1", half: true, vis: st, ph: "192.168.1.1");
            Txt(x, B("net.dns2"), "DNS 2", half: true, vis: st, ph: "8.8.8.8");
            Txt(x, B("net.ntp"), "Сервер времени (NTP)", "Оставьте пустым, чтобы не менять", false, null, "pool.ntp.org или time.example.com");
        }

        // ------------------------------------------------------------ активация

        void PageActivation()
        {
            GPage p = AddPage("activation", "Активация", "key");
            GCard c = AddCard(p, "Активация Windows при первом входе", null);
            RowCtx x = c.Ctx;
            Func<string> am = delegate() { return St.O("act").S("mode"); };
            Func<bool> kms = delegate() { return am() == "kms"; };
            Sel(x, B("act.mode"), "Способ", Opts("none", "Не активировать", "kms", "Свой KMS-сервер", "key", "Ключ продукта (онлайн)"));
            Txt(x, B("act.kmsHost"), "Адрес KMS-сервера", half: true, vis: kms, ph: "kms.example.com");
            Num(x, B("act.kmsPort"), "Порт", 1, 65535, "По умолчанию 1688", true, kms);
            Chk(x, B("act.custom"), "Использовать свой ключ вместо общего ключа KMS (GVLK)", vis: kms);
            Bind be = B("act.edition");
            GRow edRow = Sel(x, be, "Редакция (для общего ключа KMS)", new List<Opt>(), "Общие ключи KMS (GVLK) публикует Microsoft — они работают только с KMS-сервером. «Как в установке» берёт редакцию, выбранную на вкладке «Установка» (только для чистой установки).",
                vis: delegate() { return kms() && !St.O("act").B("custom"); });
            refreshers.Add(delegate
            {
                List<Opt> eds = new List<Opt>();
                eds.Add(new Opt("auto", "Как в установке (рекомендуется)"));
                foreach (Ed e in Cat.Gvlk) if (Cat.Has(e.Os, Os)) eds.Add(new Opt(e.Name, "Windows " + Os + " " + e.Name));
                FillCombo(edRow, eds, be, "auto");
            });
            Txt(x, B("act.key"), "Ключ продукта", ph: "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", vis: delegate() { return am() == "key" || (kms() && St.O("act").B("custom")); });
            Note(x, "Скрипт ждёт появления сети (до 10 минут) и делает до 5 попыток slmgr /ato. Для Home-редакции KMS-ключа нет — сначала выберите Pro.");
        }

        // ------------------------------------------------------------ приложения и программы

        void PageApps()
        {
            GPage p = AddPage("apps", "Удалить приложения", "trash");
            GCard c = AddCard(p, "Удалить предустановленные приложения", null);
            RowCtx x = c.Ctx;
            Buttons(x, new string[] { "Выбрать все", "Сбросить" }, new Action[] {
                delegate { foreach (BloatItem b in Cat.Bloat) St.O("bloat")[b.Id] = Cat.BloatOk(b, Os); LoadAll(); },
                delegate { foreach (BloatItem b in Cat.Bloat) St.O("bloat")[b.Id] = false; LoadAll(); } }, null, 110);
            Note(x, "Удаляются из образа до создания пользователей, поэтому не появятся ни у кого. «Калькулятор» и «Фотографии» тоже можно удалить — но тогда нужен другой просмотрщик и калькулятор.");
            foreach (BloatItem b in Cat.Bloat)
            {
                BloatItem it = b;
                Chk(x, BMap("bloat", it.Id, null), it.Label, it.Id.Replace(";", " + "), true, delegate() { return Cat.BloatOk(it, Os); });
            }
        }

        void PagePrograms()
        {
            GPage p = AddPage("programs", "Установить программы", "box");
            GCard c = AddCard(p, "Установка программ при первом входе", null);
            RowCtx x = c.Ctx;
            Buttons(x, new string[] { "Выбрать все", "Сбросить" }, new Action[] {
                delegate { foreach (Pkg k in Cat.Packages) St.O("apps").O("pkgs")[k.Id] = true; LoadAll(); },
                delegate { foreach (Pkg k in Cat.Packages) St.O("apps").O("pkgs")[k.Id] = false; LoadAll(); } }, null, 110);
            Note(x, "Нужен интернет: скрипт дожидается сети (до 10 минут) и ставит выбранное тихо, по очереди. Ход установки пишется в C:\\Windows\\Setup\\Scripts\\Packages.log.");
            Sel(x, B("apps.mgr"), "Менеджер пакетов", Opts("winget", "winget (Windows Package Manager)", "choco", "Chocolatey"), "winget входит в Windows 11 и новые сборки Windows 10. Chocolatey скачивается и устанавливается скриптом с community.chocolatey.org — работает везде, но запускает загружаемый скрипт.");
            foreach (Pkg k in Cat.Packages)
                Chk(x, BMap("apps.pkgs", k.Id, null), k.Name, "winget: " + k.Winget + " · choco: " + k.Choco, true);
            Txt(x, B("apps.custom"), "Свои пакеты (идентификаторы winget или Chocolatey — по одному в строке, зависят от выбранного менеджера)", lines: 3, ph: "Notepad++.Notepad++");
        }

        // ------------------------------------------------------------ скрипты и планировщик

        void PageScripts()
        {
            GPage p = AddPage("scripts", "Скрипты", "script");
            GCard c = AddCard(p, "Свои команды и скрипты", null);
            Note(c.Ctx, "Specialize — выполняется от имени SYSTEM до создания пользователей, без сети и без интерфейса. FirstLogon — при первом входе в систему, в сессии пользователя, с правами администратора. Выполняются по порядку сверху вниз.");
            ListEditor(c, "cmds", "+ Добавить команду / скрипт", "");
        }

        void PageTasks()
        {
            GPage p = AddPage("tasks", "Планировщик", "clock");
            GCard c = AddCard(p, "Задачи планировщика", null);
            Note(c.Ctx, "Задачи создаются при первом входе в систему (FirstLogon).");
            ListEditor(c, "tasks", "+ Добавить задачу", "");
        }
    }
}
