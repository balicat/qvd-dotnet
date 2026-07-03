using System.Globalization;

namespace Qvd;

/// <summary>
/// A single decoded QVD value. Qlik values are "duals": a value can carry a numeric
/// representation, a text representation, or both at once (e.g. a date is stored as
/// a numeric serial together with its formatted text).
/// </summary>
public readonly struct QvdValue : IEquatable<QvdValue>
{
    /// <summary>The NULL value.</summary>
    public static readonly QvdValue Null = default;

    private QvdValue(QvdValueKind kind, double? number, string? text)
    {
        Kind = kind;
        Number = number;
        Text = text;
    }

    /// <summary>Which symbol type this value was decoded from.</summary>
    public QvdValueKind Kind { get; }

    /// <summary>
    /// The numeric part, if any. Present for <see cref="QvdValueKind.Integer"/>,
    /// <see cref="QvdValueKind.Double"/> and both dual kinds. Integers are within
    /// int32 range and therefore held exactly.
    /// </summary>
    public double? Number { get; }

    /// <summary>
    /// The text part, if any. Present for <see cref="QvdValueKind.Text"/> and both dual kinds.
    /// </summary>
    public string? Text { get; }

    /// <summary>True when this value is NULL.</summary>
    public bool IsNull => Kind == QvdValueKind.Null;

    /// <summary>
    /// The value as Qlik would display it: the text part when present, otherwise the
    /// numeric part formatted with the invariant culture. Null for NULL values.
    /// </summary>
    public string? DisplayText => Text ?? Number?.ToString("R", CultureInfo.InvariantCulture);

    public static QvdValue FromInteger(int value) =>
        new(QvdValueKind.Integer, value, null);

    public static QvdValue FromDouble(double value) =>
        new(QvdValueKind.Double, value, null);

    public static QvdValue FromText(string text) =>
        new(QvdValueKind.Text, null, text ?? throw new ArgumentNullException(nameof(text)));

    public static QvdValue FromDualInteger(int number, string text) =>
        new(QvdValueKind.DualInteger, number, text ?? throw new ArgumentNullException(nameof(text)));

    public static QvdValue FromDualDouble(double number, string text) =>
        new(QvdValueKind.DualDouble, number, text ?? throw new ArgumentNullException(nameof(text)));

    public override string ToString() => DisplayText ?? "";

    public bool Equals(QvdValue other) =>
        Kind == other.Kind && Nullable.Equals(Number, other.Number) && Text == other.Text;

    public override bool Equals(object? obj) => obj is QvdValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Number, Text);

    public static bool operator ==(QvdValue left, QvdValue right) => left.Equals(right);

    public static bool operator !=(QvdValue left, QvdValue right) => !left.Equals(right);
}
