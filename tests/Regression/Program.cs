using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: Guid("FD430989-A6EC-42DA-89B2-DC8B41B18486")]

internal static class Program
{
    private static Assembly App;
    private static int Passed;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var executable = Path.GetFullPath(args[0]);
            var folder = Path.GetDirectoryName(executable);
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                var identity = new AssemblyName(request.Name);
                var name = identity.Name + ".dll";
                if (!string.IsNullOrEmpty(identity.CultureName))
                {
                    var satellite = Path.Combine(folder, identity.CultureName, name);
                    return File.Exists(satellite) ? Assembly.LoadFrom(satellite) : null;
                }
                var path = Path.Combine(folder, "lib", name);
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            App = Assembly.LoadFrom(executable);
            if (args.Length > 1 && args[1] == "--screenshot")
            {
                SaveLocalizedSettings(args[2], Path.GetFullPath(args[3]));
                return 0;
            }
            if (args.Length > 1 && args[1] == "--topmost") { TestAlwaysOnTop(); return 0; }
            if (args.Length > 1 && args[1] == "--awake")
            {
                TestKeepScreenActive();
                return 0;
            }
            if (args.Length > 1 && args[1] == "--layout")
            {
                TestLayout(Path.GetFullPath(args[2]));
                return 0;
            }
            if (args.Length > 1 && args[1] == "--whatsapp")
            {
                TestWhatsAppOnDesktop();
                return 0;
            }
            TestModifiers();
            TestMouseWheel();
            TestMouseRestart();
            TestShortcutHash();
            TestModifierSides();
            TestProcessNames();
            TestPortugueseResources();
            TestSettingsCheckbox();
            Console.WriteLine("PASS: " + Passed + " verificações de regressão.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void TestPortugueseResources()
    {
        var resources = App.GetType("SylphyHorn.Properties.Resources", true);
        var manager = (System.Resources.ResourceManager)resources.GetProperty("ResourceManager").GetValue(null);
        var portuguese = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        Check(manager.GetString("Settings_General", portuguese) == "Geral", "carregar tradução PT-BR da distribuição");
        var set = manager.GetResourceSet(portuguese, true, false);
        var neutral = manager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, false);
        Check(set != null, "assembly satélite PT-BR presente");
        foreach (System.Collections.DictionaryEntry entry in neutral)
            if (entry.Value is string) Check(set.GetString((string)entry.Key) != null, "tradução: " + entry.Key);
        var service = App.GetType("SylphyHorn.Services.ResourceService", true);
        var current = service.GetProperty("Current").GetValue(null);
        var cultures = (System.Collections.Generic.IEnumerable<System.Globalization.CultureInfo>)service.GetProperty("SupportedCultures").GetValue(current);
        Check(cultures.Any(c => c.Name == "pt-BR"), "PT-BR disponível no seletor");
        // Create the XAML with the selected culture to detect errors in localized bindings.
        service.GetMethod("ChangeCulture").Invoke(current, new object[] { "pt-BR" });
        Check(File.Exists(Path.Combine(Path.GetDirectoryName(App.Location), "SchedulerManager.exe")), "auxiliar de inicialização distribuído");
    }

    private static Type T(string name) => App.GetType("SylphyHorn.Services." + name, true);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Passed++;
        Console.WriteLine("PASS: " + description);
    }
    private static object Detector(HashSet<Keys> physical)
        => Activator.CreateInstance(T("ShortcutKeyDetector"), PrivateInstance, null,
            new object[] { new Func<Keys, bool>(physical.Contains) }, null);
    private static HashSet<Keys> KeysOf(object detector, string field)
        => (HashSet<Keys>)detector.GetType().GetField(field, PrivateInstance).GetValue(detector);
    private static void KeyDown(object detector, Keys key)
        => detector.GetType().GetMethod("InterceptorOnKeyDown", PrivateInstance)
            .Invoke(detector, new object[] { null, new KeyEventArgs(key) });

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);

    private static void TestWhatsAppOnDesktop()
    {
        // Exercise the real service without starting another desktop manager instance.
        var type = T("WhatsAppClickThroughService");
        var errors = new List<Exception>();
        var windows = new List<IntPtr>();
        var service = Activator.CreateInstance(type, new object[] { new Action<Exception>(error =>
        {
            lock (errors) errors.Add(error);
        }) });
        try
        {
            type.GetMethod("SetEnabled").Invoke(service, new object[] { true });
            System.Threading.Thread.Sleep(2500);
            var modified = (System.Collections.IDictionary)type.GetField("_modified", PrivateInstance).GetValue(service);
            Console.WriteLine("Janelas corrigidas: " + modified.Count);
            foreach (System.Collections.DictionaryEntry item in modified) windows.Add((IntPtr)item.Key);
            if (windows.Count == 0)
                throw new InvalidOperationException("Teste real pendente: deixar o WhatsApp aberto em outra área.");
            foreach (var window in windows)
            {
                Check((GetWindowLong(window, -20) & 0x20) != 0, "liberar hit-test da janela " + window.ToString("X"));
                // Reproduce the WebView resetting its own styles.
                SetWindowLong(window, -20, GetWindowLong(window, -20) & ~0x20);
            }
            System.Threading.Thread.Sleep(1500);
            foreach (var window in windows)
                Check((GetWindowLong(window, -20) & 0x20) != 0, "reaplicar correção após reposição do estilo");
            type.GetMethod("SetEnabled").Invoke(service, new object[] { false });
            System.Threading.Thread.Sleep(1000);
            Check(modified.Count == 0, "restaurar todas as janelas ao desativar");
            foreach (var window in windows)
                Check((GetWindowLong(window, -20) & 0x20) == 0, "restaurar estilo original ao desativar");
            type.GetMethod("SetEnabled").Invoke(service, new object[] { true });
            System.Threading.Thread.Sleep(1500);
            Check(modified.Count > 0, "reativar serviço sem criar outra thread");
        }
        finally { ((IDisposable)service).Dispose(); }
        foreach (var window in windows)
            Check((GetWindowLong(window, -20) & 0x20) == 0, "restaurar estilo original no encerramento");
        Check(errors.Count == 0, "monitoramento do WhatsApp sem exceções");
    }

    private static void TestModifiers()
    {
        var physical = new HashSet<Keys>();
        var detector = Detector(physical);
        try
        {
            KeyDown(detector, Keys.LWin); // KeyUp was lost when leaving the session.
            KeyDown(detector, Keys.D5);
            Check(KeysOf(detector, "_pressedModifiers").Count == 0, "remover modificador cujo KeyUp foi perdido");
            physical.Add(Keys.RControlKey); // KeyDown occurred before the hook was installed or resumed.
            KeyDown(detector, Keys.D5);
            Check(KeysOf(detector, "_pressedModifiers").SetEquals(physical), "recuperar modificador físico sem KeyDown observado");
            detector.GetType().GetMethod("Stop").Invoke(detector, null);
            KeyDown(detector, Keys.LWin);
            Check(KeysOf(detector, "_pressedModifiers").Count == 0, "respeitar suspensão da captura");
        }
        finally { ((IDisposable)detector).Dispose(); }
    }
    private static void TestMouseWheel()
    {
        var detector = Detector(new HashSet<Keys>());
        try
        {
            KeysOf(detector, "_pressedMouseButtons").Add(Keys.RButton);
            var stateType = T("Mouse.MouseState");
            var strokeType = T("Mouse.Stroke");
            foreach (var name in new[] { "WheelUp", "WheelUp", "WheelDown" })
            {
                var state = Activator.CreateInstance(stateType);
                var stroke = Enum.Parse(strokeType, name);
                stateType.GetField("Stroke").SetValue(state, stroke);
                stateType.GetField("KeyCode").SetValue(state, (Keys)Convert.ToInt32(stroke));
                detector.GetType().GetMethod("InterceptorOnMouseWheel", PrivateInstance)
                    .Invoke(detector, new[] { state });
                Check(KeysOf(detector, "_pressedMouseButtons").SetEquals(new[] { Keys.RButton }),
                    "roda " + name + " não permanece como botão pressionado");
            }
        }
        finally { ((IDisposable)detector).Dispose(); }
    }
    private static void TestMouseRestart()
    {
        var mouse = Activator.CreateInstance(T("Mouse.MouseInterceptor"));
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                mouse.GetType().GetMethod("StartCapturing").Invoke(mouse, null);
                Check((bool)mouse.GetType().GetProperty("IsHooking").GetValue(mouse), "instalar hook de mouse");
                mouse.GetType().GetMethod("StopCapturing").Invoke(mouse, null);
                Check(mouse.GetType().GetField("_lowLevelHookEvent", PrivateInstance).GetValue(mouse) != null,
                    "preservar callback após parar o hook");
            }
        }
        finally { ((IDisposable)mouse).Dispose(); }
    }
    private static void TestShortcutHash()
    {
        var type = T("ShortcutKey");
        var first = Activator.CreateInstance(type, Keys.D5, new[] { Keys.LWin, Keys.LControlKey });
        var second = Activator.CreateInstance(type, Keys.D5, new[] { Keys.LControlKey, Keys.LWin });
        Check(first.Equals(second), "atalhos iguais com modificadores em ordem diferente");
        Check(first.GetHashCode() == second.GetHashCode(), "atalhos iguais produzem o mesmo hash");
    }
    private static void TestSettingsCheckbox()
    {
        var application = (System.Windows.Application)Activator.CreateInstance(App.GetType("SylphyHorn.Application"), true);
        application.GetType().GetMethod("InitializeComponent").Invoke(application, null);
        var window = (System.Windows.Window)Activator.CreateInstance(App.GetType("SylphyHorn.UI.SettingsWindow"), true);
        try
        {
            var boxType = App.GetType("SylphyHorn.UI.Controls.ShortcutKeyBox");
            var box = (System.Windows.FrameworkElement)Activator.CreateInstance(boxType);
            var currentProperty = (System.Windows.DependencyProperty)boxType.GetField("CurrentProperty").GetValue(null);
            var source = new ShortcutSource { Value = new List<int> { (int)Keys.D5, (int)Keys.LControlKey } };
            System.Windows.Data.BindingOperations.SetBinding(box, currentProperty, new System.Windows.Data.Binding("Value") { Source = source, Mode = System.Windows.Data.BindingMode.TwoWay });
            boxType.GetMethod("CycleModifier", PrivateInstance).Invoke(box, new object[] { Keys.LControlKey });
            Check(System.Windows.Data.BindingOperations.IsDataBound(box, currentProperty), "clique no modificador preserva binding de persistência");
            Check(source.Value.Contains((int)Keys.RControlKey), "clique no modificador atualiza a fonte vinculada");
            var checkbox = (System.Windows.Controls.CheckBox)window.FindName("WhatsAppClickThroughCheckBox");
            Check(checkbox != null, "checkbox presente no XAML compilado");
            var binding = checkbox.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty);
            Check(binding != null, "checkbox vinculado à configuração persistente");
            binding.UpdateTarget();
            var settings = App.GetType("SylphyHorn.Serialization.Settings").GetProperty("General").GetValue(null);
            var property = settings.GetType().GetProperty("WhatsAppClickThrough").GetValue(settings);
            var value = property.GetType().GetProperty("Value");
            var before = (bool)value.GetValue(property);
            try
            {
                checkbox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, !before);
                binding.UpdateSource();
                Check((bool)value.GetValue(property) == !before, "checkbox altera a preferência");
                value.SetValue(property, before);
                binding.UpdateTarget();
                Check(checkbox.IsChecked == before, "preferência atualiza o checkbox");
            }
            finally { value.SetValue(property, before); }
        }
        finally { window.Close(); }
        var windowType = window.GetType();
        Check((bool)windowType.GetProperty("IsClosed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window), "janela encerrada identificada explicitamente");
        windowType.GetProperty("Instance").SetValue(null, null);
        for (var i = 0; i < 3; i++)
        {
            var fresh = (System.Windows.Window)Activator.CreateInstance(windowType, true);
            windowType.GetProperty("Instance").SetValue(null, fresh);
            fresh.Close();
            Check(windowType.GetProperty("Instance").GetValue(null) == null, "fechar libera a instância para reabrir " + i);
        }
    }

    public class ShortcutSource { public IList<int> Value { get; set; } }

    private static void TestModifierSides()
    {
        var type = T("ShortcutKey");
        var matcher = type.GetMethod("MatchesPressed");
        var box = App.GetType("SylphyHorn.UI.Controls.ShortcutKeyBox");
        var cycle = box.GetMethod("NextModifierSide", BindingFlags.Static | BindingFlags.NonPublic);
        var genericWin = (Keys)0x4000005B;
        foreach (var family in new[] { new[] { Keys.LShiftKey, Keys.RShiftKey, Keys.Shift }, new[] { Keys.LControlKey, Keys.RControlKey, Keys.Control }, new[] { Keys.LMenu, Keys.RMenu, Keys.Menu }, new[] { Keys.LWin, Keys.RWin, genericWin } })
        {
            for (var i = 0; i < 3; i++) Check((Keys)cycle.Invoke(null, new object[] { family[i] }) == family[(i + 1) % 3], "ciclo do modificador " + family[i]);
            var any = Activator.CreateInstance(type, Keys.D5, new[] { family[2] });
            var left = Activator.CreateInstance(type, Keys.D5, new[] { family[0] });
            var right = Activator.CreateInstance(type, Keys.D5, new[] { family[1] });
            Check((bool)matcher.Invoke(any, new[] { left }) && (bool)matcher.Invoke(any, new[] { right }), "atalho genérico aceita ambos os lados " + family[2]);
            Check(!(bool)matcher.Invoke(left, new[] { right }), "atalho específico distingue o lado " + family[0]);
        }
        var search = App.GetType("SylphyHorn.UI.Controls.SearchableComboBox").GetMethod("Matches", BindingFlags.NonPublic | BindingFlags.Static);
        Check((bool)search.Invoke(null, new object[] { "Português (Brasil)", "PORTUGUES" }), "busca ignora caixa e acentos");
        Check((bool)search.Invoke(null, new object[] { "Cáceres".Normalize(System.Text.NormalizationForm.FormD), "caceres" }), "busca aceita Unicode decomposto");
    }

    [StructLayout(LayoutKind.Sequential)] private struct IdleInfo { public uint Size; public uint Time; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref IdleInfo info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint arg, out uint value, uint flags);

    private static void TestKeepScreenActive()
    {
        SystemParametersInfo(0x0010, 0, out var initialSaver, 0);
        SystemParametersInfo(0x000E, 0, out var initialTimeout, 0);
        Exception failure = null;
        var type = T("KeepScreenActiveService");
        var service = Activator.CreateInstance(type, new object[] { new Action<Exception>(ex => failure = ex) });
        var enabled = type.GetMethod("SetEnabled");
        var active = type.GetProperty("IsActive", BindingFlags.NonPublic | BindingFlags.Instance);
        try
        {
            enabled.Invoke(service, new object[] { true });
            Check(System.Threading.SpinWait.SpinUntil(() => (bool)active.GetValue(service) || failure != null, 3000), "serviço responde à ativação");
            if (failure != null) throw failure;
            Check((bool)active.GetValue(service), "Windows aceita solicitação de tela e sistema ativos");
            System.Threading.Thread.Sleep(1200);
            var before = new IdleInfo { Size = 8 }; GetLastInputInfo(ref before);
            GetCursorPos(out var cursorBefore);
            type.GetMethod("ResetScreenSaverIdle", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            GetCursorPos(out var cursorAfter);
            Console.WriteLine("CURSOR: " + cursorBefore.X + "," + cursorBefore.Y + " -> " + cursorAfter.X + "," + cursorAfter.Y);
            System.Threading.Thread.Sleep(100);
            var after = new IdleInfo { Size = 8 }; GetLastInputInfo(ref after);
            Check(after.Time != before.Time, "evento sem deslocamento renova o temporizador real de atividade");
            Check(cursorBefore.X == cursorAfter.X && cursorBefore.Y == cursorAfter.Y, "renovação não desloca o ponteiro");
            enabled.Invoke(service, new object[] { false });
            Check(System.Threading.SpinWait.SpinUntil(() => !(bool)active.GetValue(service), 3000), "desativação libera solicitação de energia");
            enabled.Invoke(service, new object[] { true });
            Check(System.Threading.SpinWait.SpinUntil(() => (bool)active.GetValue(service) || failure != null, 3000), "reativação funciona");
        }
        finally { ((IDisposable)service).Dispose(); }
        Check(!(bool)active.GetValue(service), "encerramento libera energia");
        var worker = (System.Threading.Thread)type.GetField("_worker", PrivateInstance).GetValue(service);
        Check(!worker.IsAlive, "encerramento não deixa thread temporária");
        SystemParametersInfo(0x0010, 0, out var finalSaver, 0);
        SystemParametersInfo(0x000E, 0, out var finalTimeout, 0);
        Check(initialSaver == finalSaver && initialTimeout == finalTimeout, "configurações originais da proteção de tela preservadas");
        if (failure != null) throw failure;
    }

    // Exercise the compiled application with its real theme and data, then shut down all services.
    private static void TestLayout(string folder)
    {
        Directory.CreateDirectory(folder);
        var application = (System.Windows.Application)Activator.CreateInstance(App.GetType("SylphyHorn.Application"), true);
        application.GetType().GetMethod("InitializeComponent").Invoke(application, null);
        System.Windows.Window window = null;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        Exception failure = null;
        timer.Tick += (_, __) =>
        {
            timer.Stop();
            try
            {
                var hook = application.GetType().GetProperty("HookService", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(application);
                window = (System.Windows.Window)Activator.CreateInstance(App.GetType("SylphyHorn.UI.SettingsWindow"), true);
                window.DataContext = Activator.CreateInstance(App.GetType("SylphyHorn.UI.Bindings.SettingsWindowViewModel"), hook);
                window.Show();
                var tabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabControl");
                for (var i = 0; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    Pump(window);
                    SaveVisual(window, Path.Combine(folder, "tab-" + i.ToString("00") + "-top.png"));
                    var tab = (System.Windows.Controls.TabItem)tabs.Items[i];
                    var scroll = tab.Content as System.Windows.Controls.ScrollViewer;
                    if (scroll != null && scroll.ScrollableHeight > 0)
                    {
                        scroll.ScrollToBottom(); Pump(window);
                        SaveVisual(window, Path.Combine(folder, "tab-" + i.ToString("00") + "-bottom.png"));
                    }
                    Console.WriteLine("LAYOUT: " + i + " " + ((System.Windows.Controls.TextBlock)tab.Header).Text);
                }
                tabs.SelectedIndex = 0; Pump(window);
                var culture = (System.Windows.Controls.ComboBox)window.FindName("CultureSelector");
                culture.BringIntoView(); Pump(window);
                var selectedCulture = culture.SelectedItem;
                culture.IsDropDownOpen = true; Pump(window);
                var searchBox = (System.Windows.Controls.TextBox)culture.Template.FindName("PART_SearchBox", culture);
                var results = (System.Windows.Controls.ListBox)culture.Template.FindName("PART_SearchResults", culture);
                Check(searchBox != null && results != null, "lista real oferece busca");
                searchBox.Text = "PORTUGUES"; Pump(window);
                Check(results.Items.Count == 1, "busca real encontra Português sem acento");
                searchBox.Text = "opção inexistente"; Pump(window);
                Check(results.Items.Count == 0 && ReferenceEquals(culture.SelectedItem, selectedCulture), "busca sem resultado preserva idioma salvo");
                culture.IsDropDownOpen = false;
                var screenCheckbox = (System.Windows.Controls.CheckBox)window.FindName("KeepScreenActiveCheckBox");
                var trayForToggle = application.GetType().GetProperty("TaskTrayIcon", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(application);
                var originalChecked = screenCheckbox.IsChecked;
                try
                {
                    screenCheckbox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
                    Pump(window);
                    trayForToggle.GetType().GetMethod("RebuildContextMenu", PrivateInstance).Invoke(trayForToggle, null);
                    var notify = (NotifyIcon)trayForToggle.GetType().GetField("_notifyIcon", PrivateInstance).GetValue(trayForToggle);
                    var menu = notify.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Manter Tela Ativa");
                    Check(menu.Checked, "configuração ativa a marcação do menu da bandeja");
                    Check(menu.ToolTipText.Contains("proteção de tela") && ((string)screenCheckbox.ToolTip).Contains("tela ligada"), "dicas explicam as duas funções nas configurações e na bandeja");
                    menu.PerformClick(); Pump(window);
                    Check(screenCheckbox.IsChecked == false, "menu da bandeja desativa e atualiza a configuração aberta");
                }
                finally { screenCheckbox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, originalChecked); }
                window.Close(); window = null;
                // Open a new window after closing the previous one to exercise the real disposal callbacks.
                for (var i = 0; i < 2; i++)
                {
                    window = (System.Windows.Window)Activator.CreateInstance(App.GetType("SylphyHorn.UI.SettingsWindow"), true);
                    window.DataContext = Activator.CreateInstance(App.GetType("SylphyHorn.UI.Bindings.SettingsWindowViewModel"), hook);
                    window.Show(); Pump(window); window.Close(); window = null;
                }
                var tray = application.GetType().GetProperty("TaskTrayIcon", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(application);
                for (var i = 0; i < 3; i++)
                {
                    application.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var opened = (System.Windows.Window)App.GetType("SylphyHorn.UI.SettingsWindow").GetProperty("Instance").GetValue(null);
                        if (opened == null) throw new Exception("A bandeja não abriu as configurações");
                        opened.Close();
                    }));
                    tray.GetType().GetMethod("OnIconClick", PrivateInstance).Invoke(tray, new object[] { null, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
                    Check(App.GetType("SylphyHorn.UI.SettingsWindow").GetProperty("Instance").GetValue(null) == null, "abrir e fechar pelo caminho real da bandeja " + i);
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); application.Shutdown(); }
        };
        application.Startup += (_, __) => timer.Start();
        application.Run();
        if (failure != null) throw failure;
    }

    // Render the real XAML without starting hooks or services or persisting user settings.
    private static void TestAlwaysOnTop()
    {
        // Isolate automatic reads and writes from the user's actual preferences.
        var providerType = App.GetType("SylphyHorn.Serialization.LocalSettingsProvider");
        var provider = providerType.GetProperty("Instance").GetValue(null);
        var temporarySettings = Path.Combine(Path.GetTempPath(), "sylphy-topmost-test-" + Guid.NewGuid().ToString("N") + ".xml");
        providerType.GetField("_targetFile", PrivateInstance).SetValue(provider, new FileInfo(temporarySettings));
        var service = Activator.CreateInstance(T("AlwaysOnTopService"), true);
        var window = new System.Windows.Window { Width = 360, Height = 180, Title = "SylphyHorn - teste temporário", ShowActivated = false };
        var settings = App.GetType("SylphyHorn.Serialization.Settings").GetProperty("General").GetValue(null);
        var width = settings.GetType().GetProperty("AlwaysOnTopBorderWidth").GetValue(settings);
        var color = settings.GetType().GetProperty("AlwaysOnTopBorderColor").GetValue(settings);
        Action<string, object> call = (name, arg) => service.GetType().GetMethod(name).Invoke(service, new[] { arg });
        Func<bool> topmost = () => (TopmostStyle(new System.Windows.Interop.WindowInteropHelper(window).Handle, -20) & 8) != 0;
        var windows = (System.Collections.IDictionary)service.GetType().GetField("windows", PrivateInstance).GetValue(service);
        try
        {
            window.Show();
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Check((int)width.GetType().GetProperty("Value").GetValue(width) == 2, "borda padrão 2 px");
            Check((string)color.GetType().GetProperty("Value").GetValue(color) == "#0078D7", "borda padrão azul");
            call("Toggle", handle); Check(!topmost(), "função desativada não fixa");
            call("SetEnabled", true); call("Toggle", handle); Check(topmost(), "fixar janela real");
            var overlay = (System.Windows.Window)windows[handle];
            Check(overlay != null && overlay.IsVisible, "borda criada e visível");
            var style = TopmostStyle(new System.Windows.Interop.WindowInteropHelper(overlay).Handle, -20);
            Check((style & 0x08000020) == 0x08000020, "borda sem foco e transparente aos cliques");
            width.GetType().GetProperty("Value").SetValue(width, 0);
            service.GetType().GetMethod("Refresh", PrivateInstance).Invoke(service, null);
            Check(topmost() && !overlay.IsVisible, "0 px oculta apenas a borda");
            width.GetType().GetProperty("Value").SetValue(width, 5);
            color.GetType().GetProperty("Value").SetValue(color, "#FF0000");
            service.GetType().GetMethod("Refresh", PrivateInstance).Invoke(service, null);
            Check(overlay.IsVisible && ((System.Windows.Media.SolidColorBrush)((System.Windows.Controls.Border)overlay.Content).BorderBrush).Color.R == 255, "alteração de cor e espessura aplicada");
            call("Toggle", handle); Check(!topmost() && windows.Count == 0, "atalho alterna para desafixar");
            call("Toggle", handle); call("SetEnabled", false); Check(!topmost() && windows.Count == 0, "desativar restaura janelas");
            call("SetEnabled", true); call("Toggle", handle); ((IDisposable)service).Dispose();
            Check(!topmost(), "encerramento remove fixação própria");
            Console.WriteLine("TOPMOST: " + Passed + " verificações aprovadas");
        }
        finally { ((IDisposable)service).Dispose(); window.Close(); if (File.Exists(temporarySettings)) File.Delete(temporarySettings); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int TopmostStyle(IntPtr handle, int index);

    private static void SaveLocalizedSettings(string culture, string path)
    {
        var application = (System.Windows.Application)Activator.CreateInstance(App.GetType("SylphyHorn.Application"), true);
        application.GetType().GetMethod("InitializeComponent").Invoke(application, null);
        App.GetType("SylphyHorn.Properties.Resources").GetProperty("Culture").SetValue(null, System.Globalization.CultureInfo.GetCultureInfo(culture));
        var window = (System.Windows.Window)Activator.CreateInstance(App.GetType("SylphyHorn.UI.SettingsWindow"), true);
        var content = (System.Windows.FrameworkElement)window.Content;
        content.Measure(new System.Windows.Size(window.Width, window.Height));
        content.Arrange(new System.Windows.Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var background = new System.Windows.Media.DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background ?? System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, window.Width, window.Height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        window.Close();
        Console.WriteLine("SCREENSHOT: " + culture + " " + path);
    }

    private static void Pump(System.Windows.Window window)
    {
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
        window.UpdateLayout();
    }

    private static void SaveVisual(System.Windows.Window window, string path)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }

    private static void TestProcessNames()
    {
        var normalize = T("ApplicationRoutingService").GetMethod("NormalizeProcessName", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var name in new[] { "WhatsApp.Root", "WhatsApp.Root.exe", @"C:\Apps\WhatsApp.Root.exe" })
            Check((string)normalize.Invoke(null, new object[] { name }) == "WhatsApp.Root",
                "preservar ponto no nome do processo " + name);
    }
}
