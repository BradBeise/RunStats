using System;
using System.Numerics;

namespace RunStats.Tracking;

internal readonly struct ExactFraction : IComparable<ExactFraction>
{
    public static readonly ExactFraction Zero = new(BigInteger.Zero, BigInteger.One);

    public ExactFraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public BigInteger Numerator { get; }

    public BigInteger Denominator { get; }

    public bool IsPositive => Numerator > BigInteger.Zero;

    public BigInteger Floor() => Numerator >= BigInteger.Zero
        ? Numerator / Denominator
        : -((-Numerator + Denominator - BigInteger.One) / Denominator);

    public int CompareTo(ExactFraction other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static ExactFraction FromRatio(long numerator, long denominator) =>
        new(numerator, denominator);

    public static ExactFraction FromProduct(long first, long second, long denominator) =>
        new((BigInteger)first * second, denominator);

    public static ExactFraction operator +(ExactFraction left, ExactFraction right) =>
        new(
            (left.Numerator * right.Denominator) + (right.Numerator * left.Denominator),
            left.Denominator * right.Denominator);

    public static ExactFraction operator -(ExactFraction left, ExactFraction right) =>
        new(
            (left.Numerator * right.Denominator) - (right.Numerator * left.Denominator),
            left.Denominator * right.Denominator);

    public static ExactFraction operator -(ExactFraction value) =>
        new(-value.Numerator, value.Denominator);

    public static implicit operator ExactFraction(long value) => new(value, BigInteger.One);
}
