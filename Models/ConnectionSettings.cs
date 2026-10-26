namespace SshTunnelManager.Models;

public sealed class ConnectionSettings
{
    public string ProfileName { get; set; } = "Minha VM Ubuntu";
    public string Host { get; set; } = "MEU-IP";
    public string User { get; set; } = "ubuntu";
    public string KeyPath { get; set; } = "ssh-key.key";
    public int LocalPort { get; set; } = 8080;
    public string RemoteHost { get; set; } = "127.0.0.1";
    public int RemotePort { get; set; } = 8080;
    public string BrowserUrl { get; set; } = "http://127.0.0.1:8080";
    public string SshExecutable { get; set; } = "ssh.exe";
}
