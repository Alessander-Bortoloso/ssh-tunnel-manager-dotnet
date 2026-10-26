using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SshTunnelManager.Models;
using SshTunnelManager.Services;

namespace SshTunnelManager;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly SshTunnelService _sshService = new();
    private readonly ObservableCollection<string> _logs = new();
    private ConnectionSettings _settings;
    private bool _isConnecting;
    private bool _isConnected;
    private bool _isClosing;
    private bool _browserOpenedForCurrentSession;
    private readonly string _appDirectory = AppContext.BaseDirectory;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        LogsList.ItemsSource = _logs;
        PopulateSettingsFields(_settings);
        UpdateConnectionSummary();
        _sshService.LogReceived += message => Dispatcher.Invoke(() => AddLog(message));
        _sshService.StateChanged += (connected, message) => Dispatcher.Invoke(() =>
        {
            if (_isClosing) return;
            if (!connected && !_isConnecting) SetConnected(false, message);
            FooterText.Text = message;
        });
        _sshService.TunnelReady += () => Dispatcher.Invoke(() =>
        {
            if (_isClosing) return;
            _isConnecting = false;
            SetConnected(true, "Túnel SSH ativo");
            if (!_browserOpenedForCurrentSession)
            {
                _browserOpenedForCurrentSession = true;
                OpenBrowser();
            }
        });
        AddLog("Aplicação iniciada.");
        AddLog($"Configurações: %LOCALAPPDATA%\\SshTunnelManager\\settings.json");
    }

    private void PopulateSettingsFields(ConnectionSettings s)
    {
        ProfileNameBox.Text = s.ProfileName;
        HostBox.Text = s.Host;
        UserBox.Text = s.User;
        KeyPathBox.Text = s.KeyPath;
        LocalPortBox.Text = s.LocalPort.ToString();
        RemoteHostBox.Text = s.RemoteHost;
        RemotePortBox.Text = s.RemotePort.ToString();
        BrowserUrlBox.Text = s.BrowserUrl;
        SshExecutableBox.Text = s.SshExecutable;
    }

    private ConnectionSettings ReadSettingsFields()
    {
        if (!int.TryParse(LocalPortBox.Text.Trim(), out int localPort))
            throw new InvalidOperationException("A porta local precisa ser um número inteiro.");
        if (!int.TryParse(RemotePortBox.Text.Trim(), out int remotePort))
            throw new InvalidOperationException("A porta remota precisa ser um número inteiro.");
        return new ConnectionSettings
        {
            ProfileName = ProfileNameBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            User = UserBox.Text.Trim(),
            KeyPath = KeyPathBox.Text.Trim(),
            LocalPort = localPort,
            RemoteHost = RemoteHostBox.Text.Trim(),
            RemotePort = remotePort,
            BrowserUrl = BrowserUrlBox.Text.Trim(),
            SshExecutable = string.IsNullOrWhiteSpace(SshExecutableBox.Text) ? "ssh.exe" : SshExecutableBox.Text.Trim()
        };
    }

    private void SaveSettings(bool showMessage)
    {
        var settings = ReadSettingsFields();
        _settingsService.Save(settings);
        _settings = settings;
        UpdateConnectionSummary();
        AddLog("Configurações salvas.");
        FooterText.Text = "Configurações salvas";
        if (showMessage) MessageBox.Show(this, "Configurações salvas.", "SSH Tunnel Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnecting) return;
        if (_isConnected)
        {
            await DisconnectAsync();
            return;
        }

        try
        {
            SaveSettings(showMessage: false);
            _isConnecting = true;
            _browserOpenedForCurrentSession = false;
            ConnectButton.Content = "Conectando…";
            ConnectButton.IsEnabled = false;
            SetStatus("Conectando…", "#B7791F", "#FFF7E6");
            ConnectionHintText.Text = "Aguardando confirmação do túnel SSH…";
            FooterText.Text = "Iniciando ssh.exe";
            MainTabs.SelectedIndex = 0;
            await _sshService.ConnectAsync(_settings, _appDirectory);
        }
        catch (Exception ex)
        {
            AddLog("ERRO: " + ex.Message);
            _isConnecting = false;
            SetConnected(false, "Falha na conexão");
            ConnectionHintText.Text = "Falha ao conectar. Consulte a aba Logs.";
            FooterText.Text = "Falha na conexão";
            MessageBox.Show(this, ex.Message, "Falha na conexão SSH", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!_isClosing) ConnectButton.IsEnabled = true;
        }
    }

    private async Task DisconnectAsync()
    {
        _isConnecting = false;
        _browserOpenedForCurrentSession = false;
        await _sshService.DisconnectAsync();
        SetConnected(false, "Desconectado");
        ConnectionHintText.Text = "Túnel encerrado. Clique em Conectar para iniciar uma nova sessão.";
        FooterText.Text = "Desconectado";
    }

    private void SetConnected(bool connected, string message)
    {
        _isConnected = connected;
        _isConnecting = false;
        ConnectButton.Content = connected ? "Desconectar" : "Conectar";
        ConnectButton.Style = connected ? (Style)FindResource(typeof(Button)) : (Style)FindResource("PrimaryButton");
        OpenBrowserButton.IsEnabled = connected;
        ConnectionHintText.Text = connected ? "O túnel está ativo. Fechar esta janela encerra a conexão." : "Desconectado. Configure os dados na aba Settings, se necessário.";
        if (connected) SetStatus("Conectado", "#16803D", "#EAF7EE");
        else if (message.Contains("Conectando", StringComparison.OrdinalIgnoreCase)) SetStatus("Conectando…", "#B7791F", "#FFF7E6");
        else SetStatus("Desconectado", "#C24141", "#FCEFEF");
        FooterText.Text = message;
    }

    private void SetStatus(string text, string foreground, string background)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground));
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground));
        if (StatusText.Parent is Border border)
            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
    }

    private void UpdateConnectionSummary()
    {
        ProfileNameText.Text = string.IsNullOrWhiteSpace(_settings.ProfileName) ? "Conexão SSH" : _settings.ProfileName;
        ConnectionSummaryText.Text = $"{_settings.User}@{_settings.Host} · 127.0.0.1:{_settings.LocalPort} → {_settings.RemoteHost}:{_settings.RemotePort}";
        LocalAddressText.Text = $"127.0.0.1:{_settings.LocalPort}";
        RemoteAddressText.Text = $"{_settings.RemoteHost}:{_settings.RemotePort}";
        BrowserUrlText.Text = _settings.BrowserUrl;
    }

    private void OpenBrowserButton_Click(object sender, RoutedEventArgs e) => OpenBrowser();

    private void OpenBrowser()
    {
        try
        {
            if (!Uri.TryCreate(_settings.BrowserUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("A URL configurada não é válida. Verifique a aba Settings.");
            Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
            AddLog($"Navegador aberto: {uri}");
        }
        catch (Exception ex)
        {
            AddLog("ERRO ao abrir o navegador: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Não foi possível abrir o navegador", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try { SaveSettings(showMessage: true); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Configurações inválidas", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Restaurar os valores padrão? Isso substituirá os campos atuais.", "Restaurar padrão", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings = new ConnectionSettings();
        PopulateSettingsFields(_settings);
        SaveSettings(showMessage: false);
        AddLog("Campos restaurados para os valores padrão.");
    }

    private void BrowseKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione a chave privada SSH",
            Filter = "Todos os arquivos (*.*)|*.*|Arquivos de chave (*.*key)|*key",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) KeyPathBox.Text = dialog.FileName;
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e) => _logs.Clear();

    private void AddLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.Add(line);
        while (_logs.Count > 2000) _logs.RemoveAt(0);
        if (LogsList.Items.Count > 0) LogsList.ScrollIntoView(LogsList.Items[^1]);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        try
        {
            await _sshService.DisconnectAsync("Aplicação encerrada; túnel SSH desconectado.");
        }
        finally
        {
            _sshService.Dispose();
        }
    }
}
