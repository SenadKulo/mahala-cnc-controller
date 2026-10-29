using System.IO.Ports;
using System.Text;

namespace GrblCore;

/// <summary>Grbl on a COM port, 8N1 without flow control.</summary>
public sealed class SerialTransport : IGrblTransport
{
    readonly SerialPort _port;

    SerialTransport(SerialPort port) => _port = port;

    /// <summary>
    /// Opens the port without DTR, so the Arduino isn't restarted by connecting.
    /// Throws IOException, UnauthorizedAccessException or ArgumentException if the port can't be opened.
    /// </summary>
    public static SerialTransport Open(string portName, int baudRate)
    {
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.ASCII,
            NewLine = "\n",
            ReadTimeout = 500,
            WriteTimeout = 2000,
        };
        port.Open();
        return new SerialTransport(port);
    }

    public bool IsOpen => _port.IsOpen;

    public bool IsPresent => SerialPort.GetPortNames().Contains(_port.PortName, StringComparer.OrdinalIgnoreCase);

    public string? ReadLine()
    {
        try
        {
            return _port.ReadLine();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Write(byte[] data) => _port.Write(data, 0, data.Length);

    public void Dispose()
    {
        try { _port.Close(); }
        catch (IOException) { } // port may already be gone (USB pulled)
        _port.Dispose();
    }
}
