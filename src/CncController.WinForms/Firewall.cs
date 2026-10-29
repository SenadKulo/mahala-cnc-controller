using System.ComponentModel;
using System.Diagnostics;

namespace CncController.WinForms;

/// <summary>
/// What Windows lets in for the page in the local network: whether a firewall rule allows the port, adding one
/// (Windows asks the user, UAC), and which connected networks Windows treats as public (no incoming connections).
/// Read through Windows' own COM objects, which need no administrator; a failure answers "don't know" (null).
/// </summary>
static class Firewall
{
    const int ProtocolTcp = 6, ProtocolAny = 256;
    const int ProfilePrivate = 2;
    const int DirectionIn = 1, ActionAllow = 1;
    const int UacCancelled = 1223;

    /// <summary>The name of the rule this program adds.</summary>
    public static string RuleName(int port) => $"Mahala CNC ({port})";

    /// <summary>An enabled rule lets TCP in on <paramref name="port"/> on private networks (for anything, or for this program); null if it can't be read.</summary>
    public static bool? IsPortAllowed(int port)
    {
        try
        {
            dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;
            foreach (dynamic rule in policy.Rules)
            {
                if (!(bool)rule.Enabled || (int)rule.Direction != DirectionIn || (int)rule.Action != ActionAllow
                    || (int)rule.Protocol is not (ProtocolTcp or ProtocolAny) || ((int)rule.Profiles & ProfilePrivate) == 0)
                    continue;
                // This program (any port), or the port named outright for everything; rules with no port belong
                // to a Store app or a service, not to us.
                string? program = rule.ApplicationName;
                string? service = rule.serviceName;
                if (string.Equals(program, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (program is null && service is null && PortListed((string?)rule.LocalPorts, port))
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>"8090", "80,8000-8100": whether the port is named in it ("*" or nothing is not a port rule).</summary>
    static bool PortListed(string? ports, int port)
    {
        if (ports is null or "" or "*")
            return false;
        foreach (var part in ports.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-');
            if (int.TryParse(range[0], out var from) && int.TryParse(range[^1], out var to) && port >= from && port <= to)
                return true;
        }
        return false;
    }

    /// <summary>Adds a rule for TCP <paramref name="port"/> on private networks; Windows asks the user first. False if refused or failed.</summary>
    public static bool AllowPort(int port)
    {
        var start = new ProcessStartInfo("netsh",
            $"advfirewall firewall add rule name=\"{RuleName(port)}\" dir=in action=allow protocol=TCP localport={port} profile=private")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var netsh = Process.Start(start);
            if (netsh is null)
                return false;
            netsh.WaitForExit(15_000);
            return netsh.HasExited && netsh.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelled)
        {
            return false;
        }
    }

    /// <summary>
    /// Firewalls of other programs (ESET, Kaspersky, Norton...) registered with Windows Security: when one is on,
    /// it filters instead of Windows' firewall, and the rule added here doesn't count. Empty if none or unknown.
    /// </summary>
    public static IReadOnlyList<string> OtherFirewalls()
    {
        try
        {
            dynamic locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", throwOnError: true)!)!;
            dynamic center = locator.ConnectServer(".", @"root\SecurityCenter2");
            var names = new List<string>();
            foreach (dynamic product in center.ExecQuery("SELECT displayName FROM FirewallProduct"))
                names.Add((string)product.displayName);
            return names;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or NotSupportedException)
        {
            // Windows Server has no Security Center.
            return [];
        }
    }

    /// <summary>Names of connected networks Windows treats as public; null if it can't be read.</summary>
    public static IReadOnlyList<string>? PublicNetworks()
    {
        try
        {
            // NetworkListManager
            dynamic manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), throwOnError: true)!)!;
            var names = new List<string>();
            const int connected = 1, categoryPublic = 0;
            foreach (dynamic network in manager.GetNetworks(connected))
            {
                if ((int)network.GetCategory() == categoryPublic)
                    names.Add((string)network.GetName());
            }
            return names;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
