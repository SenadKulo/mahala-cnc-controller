namespace GrblCore;

public readonly record struct Position(double X, double Y, double Z)
{
    public static Position operator +(Position a, Position b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Position operator -(Position a, Position b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public override string ToString() => FormattableString.Invariant($"X{Show(X):0.000} Y{Show(Y):0.000} Z{Show(Z):0.000}");

    // Rounded first so -0.0001 shows as 0.000, not -0.000 (adding 0.0 turns -0 into +0).
    static double Show(double value) => Math.Round(value, 3) + 0.0;
}
