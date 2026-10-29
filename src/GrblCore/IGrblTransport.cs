namespace GrblCore;

/// <summary>
/// Byte stream to a Grbl board: a serial port, or the simulator.
/// <see cref="GrblConnection"/> reads lines on its reader thread and writes from any thread.
/// </summary>
public interface IGrblTransport : IDisposable
{
    bool IsOpen { get; }

    /// <summary>
    /// False once the device is gone (a pulled USB cable). Checked after a read times out,
    /// because a vanished port doesn't always make the read throw.
    /// </summary>
    bool IsPresent { get; }

    /// <summary>
    /// Next line from Grbl without the line ending, or null if nothing arrived within the read timeout.
    /// Throws (IOException, InvalidOperationException, ObjectDisposedException...) when the connection breaks or is closed.
    /// </summary>
    string? ReadLine();

    /// <summary>Throws IOException, InvalidOperationException or TimeoutException when the connection is broken.</summary>
    void Write(byte[] data);
}
