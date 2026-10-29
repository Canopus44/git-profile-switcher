using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GitProfileSwitcher.Core;

namespace GitProfileSwitcher.Desktop;

public partial class MainWindow : Window
{
    readonly ProfileStore _store = new();
    readonly ObservableCollection<ProfileRow> _rows = [];
    readonly GitConfigService _git = new(new ProcessRunner());
    readonly ProfileApplier _applier;
    readonly SystemProfileScanner _scanner = new();
    readonly TranslateTransform _detailShift = new();

    string? _activeProfileId;
    Profile? _editing;
    bool _suppressSelection;
    bool _creatingNew;
    bool _busy;
    bool _firstDetailRender = true;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GitProfileSwitcher/Assets/icon.ico")));
        _applier = new ProfileApplier(new ProcessRunner(), new SshConfigManager(), AppPaths.SshConfigPath());
        ProfileList.ItemsSource = _rows;
        _detailShift.Transitions =
        [
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new CubicEaseOut()
            }
        ];
        DetailForm.RenderTransform = _detailShift;
        EnableAcrylicOnWindows11();
        _ = InitializeAsync();
    }

    async Task InitializeAsync()
    {
        try
        {
            var result = await Task.Run(_store.Load);
            _rows.Clear();
            foreach (var profile in result.Data.Profiles)
                _rows.Add(new ProfileRow(profile));

            _activeProfileId = result.Data.ActiveProfileId;
            UpdateEmptyState();

            var scan = await Task.Run(() => _scanner.Scan(HomeDirectory()));
            PopulateGhAccounts(scan.GhAccounts);

            var status = new List<(string Message, StepStatus Status)>();
            if (result.Error is not null)
                status.Add((result.Error, StepStatus.Error));

            if (_rows.Count == 0)
            {
                var imported = await ImportDiscoveredAsync(scan);
                if (imported > 0)
                    status.Add(($"{imported} perfiles detectados en tu sistema", StepStatus.Ok));
                status.AddRange(scan.Notes.Select(note => (note, StepStatus.Warning)));
            }

            var selected = _rows.FirstOrDefault(row => row.Profile.Id == _activeProfileId) ?? _rows.FirstOrDefault();
            if (selected is not null)
            {
                SetSelection(selected);
                LoadIntoForm(selected.Profile);
            }

            await RefreshHeaderAsync();

            if (status.Count > 0)
                ShowLines(status);
        }
        catch (Exception ex)
        {
            ShowLines([($"Error al iniciar: {ex.Message}", StepStatus.Error)]);
        }
    }

    void OnProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        DeleteButton.IsEnabled = ProfileList.SelectedItem is ProfileRow;
        if (_suppressSelection || ProfileList.SelectedItem is not ProfileRow row)
            return;

        _creatingNew = false;
        LoadIntoForm(row.Profile);
        ClearValidation();
        AnimateDetail();
    }

    void OnNew(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        _editing = null;
        _creatingNew = true;
        SetSelection(null);
        ClearForm();
        ClearValidation();
        UpdateEmptyState();
        AnimateDetail();
        NameBox.Focus();
        ShowLines([("Nuevo perfil. Completa los campos y guarda.", StepStatus.Ok)]);
    }

    async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (ProfileList.SelectedItem is not ProfileRow row)
        {
            ShowLines([("Selecciona un perfil para eliminar.", StepStatus.Warning)]);
            return;
        }

        _rows.Remove(row);
        if (_activeProfileId == row.Profile.Id)
            _activeProfileId = null;

        _editing = null;
        _creatingNew = false;
        SetSelection(null);
        ClearForm();
        ClearValidation();
        UpdateEmptyState();
        await SaveStoreAsync();
        await RefreshHeaderAsync();
        ShowLines([($"Perfil '{row.Profile.Name}' eliminado.", StepStatus.Ok)]);
    }

    async void OnImportFromSystem(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        SetBusy(true);
        try
        {
            var scan = await Task.Run(() => _scanner.Scan(HomeDirectory()));
            PopulateGhAccounts(scan.GhAccounts);

            var imported = await ImportDiscoveredAsync(scan);
            var lines = new List<(string Message, StepStatus Status)>
            {
                imported > 0
                    ? ($"{imported} importados", StepStatus.Ok)
                    : ("No se encontraron perfiles nuevos", StepStatus.Warning)
            };
            lines.AddRange(scan.Notes.Select(note => (note, StepStatus.Warning)));
            ShowLines(lines);
        }
        catch (Exception ex)
        {
            ShowLines([($"Error al importar: {ex.Message}", StepStatus.Error)]);
        }
        finally
        {
            SetBusy(false);
        }
    }

    async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var saved = await SaveProfileAsync();
        if (saved is null)
            return;

        await RefreshHeaderAsync();
        ShowLines([($"Perfil '{saved.Name}' guardado.", StepStatus.Ok)]);
    }

    async void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var profile = await SaveProfileAsync();
        if (profile is null)
            return;

        SetBusy(true);
        try
        {
            var report = await Task.Run(() => _applier.Apply(profile));
            ShowReport(report);

            if (report.Success)
            {
                _activeProfileId = profile.Id;
                await SaveStoreAsync();
            }

            await RefreshHeaderAsync();
        }
        catch (Exception ex)
        {
            ShowLines([($"Error inesperado al aplicar: {ex.Message}", StepStatus.Error)]);
        }
        finally
        {
            SetBusy(false);
        }
    }

    async void OnBrowseSsh(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Seleccionar llave SSH",
                AllowMultiple = false
            });

            var file = files.FirstOrDefault();
            if (file is null)
                return;

            SshBox.Text = file.TryGetLocalPath() ?? file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            ShowLines([($"No se pudo abrir el selector: {ex.Message}", StepStatus.Error)]);
        }
    }

    async Task<Profile?> SaveProfileAsync()
    {
        ClearValidation();
        var draft = new Profile
        {
            Id = _editing?.Id ?? Guid.NewGuid().ToString("D"),
            Name = NameBox.Text?.Trim() ?? "",
            UserName = GitNameBox.Text?.Trim() ?? "",
            UserEmail = EmailBox.Text?.Trim() ?? "",
            SshKeyPath = BlankToNull(SshBox.Text),
            GhAccount = BlankToNull(GhBox.Text)
        };

        var errors = draft.Validate();
        if (errors.Count > 0)
        {
            ShowValidation(errors);
            ShowLines(errors.Select(error => (error, StepStatus.Error)));
            return null;
        }

        var index = _editing is null ? -1 : IndexOfProfile(_editing.Id);
        var row = new ProfileRow(draft);
        if (index >= 0)
            _rows[index] = row;
        else
            _rows.Add(row);

        _editing = draft;
        _creatingNew = false;
        SetSelection(row);
        RefreshRowStates();
        UpdateEmptyState();
        LoadIntoForm(draft);

        return await SaveStoreAsync() ? draft : null;
    }

    async Task<bool> SaveStoreAsync()
    {
        try
        {
            var snapshot = _rows.Select(row => row.Profile).ToList();
            var activeId = _activeProfileId;
            await Task.Run(() => _store.Save(snapshot, activeId));
            return true;
        }
        catch (Exception ex)
        {
            ShowLines([($"No se pudo guardar: {ex.Message}", StepStatus.Error)]);
            return false;
        }
    }

    async Task<int> ImportDiscoveredAsync(ScanResult scan)
    {
        var existing = _rows.Select(row => row.Profile).ToList();
        var merged = ProfileImport.Merge(existing, scan.Profiles);
        var imported = merged.Skip(existing.Count).ToList();
        if (imported.Count == 0)
            return 0;

        foreach (var profile in imported)
            _rows.Add(new ProfileRow(profile));

        RefreshRowStates();
        UpdateEmptyState();
        await SaveStoreAsync();

        var first = _rows.First(row => row.Profile.Id == imported[0].Id);
        _editing = first.Profile;
        _creatingNew = false;
        SetSelection(first);
        LoadIntoForm(first.Profile);
        AnimateDetail();
        return imported.Count;
    }

    void PopulateGhAccounts(IReadOnlyList<string> accounts) => GhBox.ItemsSource = accounts.ToList();

    static string HomeDirectory() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    async Task RefreshHeaderAsync()
    {
        var active = _rows.FirstOrDefault(row => row.Profile.Id == _activeProfileId)?.Profile;
        RefreshRowStates();

        ActiveName.Text = active?.Name ?? "Sin perfil activo";
        ActiveDot.IsVisible = active is not null;
        ActiveChip.Classes.Set("inactive", active is null);

        if (active is null)
        {
            DesyncPill.IsVisible = false;
            return;
        }

        var actual = await Task.Run(() => _git.GetGlobal("user.email"));
        var desynced = !string.Equals(actual, active.UserEmail, StringComparison.OrdinalIgnoreCase);
        DesyncPill.IsVisible = desynced;
        ToolTip.SetTip(DesyncPill, $"git global = {actual ?? "(sin configurar)"}");
    }

    void RefreshRowStates()
    {
        foreach (var row in _rows)
            row.IsActive = row.Profile.Id == _activeProfileId;
    }

    int IndexOfProfile(string id)
    {
        for (var i = 0; i < _rows.Count; i++)
            if (_rows[i].Profile.Id == id)
                return i;
        return -1;
    }

    void SetSelection(ProfileRow? row)
    {
        _suppressSelection = true;
        ProfileList.SelectedItem = row;
        _suppressSelection = false;
        DeleteButton.IsEnabled = row is not null;
    }

    void LoadIntoForm(Profile profile)
    {
        _editing = profile;
        NameBox.Text = profile.Name;
        GitNameBox.Text = profile.UserName;
        EmailBox.Text = profile.UserEmail;
        SshBox.Text = profile.SshKeyPath ?? "";
        GhBox.Text = profile.GhAccount ?? "";
    }

    void ShowReport(ApplyReport report)
    {
        ClearStatus();
        foreach (var step in report.Steps)
            AddStatusRow(step.Status, step.Name, step.Message);

        AddStatusRow(
            report.Success ? StepStatus.Ok : StepStatus.Error,
            report.Success ? "Perfil aplicado" : "Aplicación con errores",
            null);
    }

    void ShowLines(IEnumerable<(string Message, StepStatus Status)> lines)
    {
        ClearStatus();
        foreach (var (message, status) in lines)
            AddStatusRow(status, message, null);
    }

    void ClearStatus()
    {
        for (var i = StatusPanel.Children.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(StatusPanel.Children[i], StatusEmpty))
                StatusPanel.Children.RemoveAt(i);

        StatusEmpty.IsVisible = true;
    }

    void AddStatusRow(StepStatus status, string title, string? detail)
    {
        StatusEmpty.IsVisible = false;

        var glyph = status switch
        {
            StepStatus.Ok => "✓",
            StepStatus.Warning => "⚠",
            _ => "✕"
        };
        var brush = ThemeBrush(status switch
        {
            StepStatus.Ok => "SuccessText",
            StepStatus.Warning => "WarningText",
            _ => "DangerText"
        });

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(detail))
            text.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 12,
                Foreground = ThemeBrush("TextSecondary"),
                TextWrapping = TextWrapping.Wrap
            });

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*") };
        row.Children.Add(new TextBlock { Text = glyph, FontSize = 12, Foreground = brush });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        StatusPanel.Children.Add(row);
    }

    static IBrush ThemeBrush(string key) => FindBrush(key) ?? Brushes.Gray;

    static IBrush? FindBrush(string key) =>
        Application.Current is { } app && app.TryFindResource(key, out var value) ? value as IBrush : null;

    void UpdateEmptyState()
    {
        var showEmpty = _rows.Count == 0 && !_creatingNew;
        EmptyPanel.IsVisible = showEmpty;
        DetailForm.IsVisible = !showEmpty;
        ActionBar.IsVisible = !showEmpty;
        ApplyButton.IsEnabled = !_busy && _rows.Count > 0;
        ProfileCount.Text = _rows.Count.ToString();
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        SaveButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && _rows.Count > 0;
    }

    void ShowValidation(IReadOnlyList<string> errors)
    {
        SetError(NameError, errors.FirstOrDefault(error => error.Contains("El nombre")));
        SetError(GitNameError, errors.FirstOrDefault(error => error.StartsWith("git user.name")));
        SetError(EmailError, errors.FirstOrDefault(error => error.StartsWith("git user.email")));
    }

    void ClearValidation()
    {
        SetError(NameError, null);
        SetError(GitNameError, null);
        SetError(EmailError, null);
    }

    static void SetError(TextBlock block, string? message)
    {
        block.Text = message ?? "";
        block.IsVisible = message is not null;
    }

    void ClearForm()
    {
        NameBox.Text = "";
        GitNameBox.Text = "";
        EmailBox.Text = "";
        SshBox.Text = "";
        GhBox.Text = "";
    }

    // Content swap: fade + 8px rise, critically damped. First paint is static.
    void AnimateDetail()
    {
        if (_firstDetailRender)
        {
            _firstDetailRender = false;
            return;
        }

        DetailForm.Opacity = 0;
        _detailShift.Y = 8;
        DispatcherTimer.RunOnce(() =>
        {
            DetailForm.Opacity = 1;
            _detailShift.Y = 0;
        }, TimeSpan.FromMilliseconds(16));
    }

    // Acrylic only where the compositor supports it; solid Bg everywhere else.
    void EnableAcrylicOnWindows11()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        try
        {
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur];
            if (FindBrush("BgTranslucent") is { } translucent)
                Background = translucent;
        }
        catch
        {
            Background = ThemeBrush("Bg");
        }
    }

    static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ProfileRow : INotifyPropertyChanged
{
    bool _isActive;

    public ProfileRow(Profile profile) => Profile = profile;

    public Profile Profile { get; }
    public string Name => Profile.Name;
    public string Email => Profile.UserEmail;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;

            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
