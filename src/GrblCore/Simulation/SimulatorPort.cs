using System.Collections.Concurrent;

namespace GrblCore.Simulation;

/// <summary>
/// The simulator's end of a connection, as <see cref="GrblConnection"/> sees it.
/// Closing it doesn't stop the simulated machine, the same way closing a COM port doesn't stop the Arduino.
/// </summary>
public sealed class SimulatorPort : IGrblTransport
{
    readonly GrblSimulator _simulator;
    readonly BlockingCollection<string> _lines = new();
    volatile bool _closed;
    volatile bool _unplugged;

    internal SimulatorPort(GrblSimulator simulator) => _simulator = simulator;

    /// <summary>How long <see cref="ReadLine"/> waits for a line, ms (as SerialPort.ReadTimeout).</summary>
    public int ReadTimeout { get; set; } = 500;

    public bool IsOpen => !_closed;

    public bool IsPresent => !_unplugged;

    public string? ReadLine()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        try
        {
            return _lines.TryTake(out var line, ReadTimeout) ? line : null;
        }
        catch (InvalidOperationException)
        {
            // Closed while waiting.
            throw new ObjectDisposedException(nameof(SimulatorPort));
        }
    }

    public void Write(byte[] data)
    {
        if (_closed)
            throw new InvalidOperationException("The port is closed.");
        if (_unplugged)
            throw new IOException("The device is not connected.");
        _simulator.Receive(this, data);
    }

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _simulator.Detach(this);
        _lines.CompleteAdding();
    }

    internal void Deliver(string line)
    {
        if (_closed || _unplugged)
            return;
        try
        {
            _lines.Add(line);
        }
        catch (InvalidOperationException)
        {
            // Closed meanwhile.
        }
    }

    internal void Unplug() => _unplugged = true;
}
