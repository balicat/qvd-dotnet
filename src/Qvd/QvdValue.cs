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

    /// <summary>
    /// A date/timestamp as Qlik stores it: a dual with the date serial number
    /// (days since 1899-12-30, time of day as the fraction) and its formatted text.
    /// Default format is yyyy-MM-dd, or yyyy-MM-dd HH:mm:ss when there is a time part.
    /// </summary>
    public static QvdValue FromDateTime(DateTime value, string? format = null)
    {
        double serial = (value - QlikEpoch).TotalDays;
        format ??= value.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss";
        return FromDualDouble(serial, value.ToString(format, CultureInfo.InvariantCulture));
    }

    private static readonly DateTime QlikEpoch = new(1899, 12, 30);

    public override string ToString() => DisplayText ?? "";

    public bool Equals(QvdValue other) =>
        Kind == other.Kind && Nullable.Equals(Number, other.Number) && Text == other.Text;

    public override bool Equals(object? obj) => obj is QvdValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Number, Text);

    public static bool operator ==(QvdValue left, QvdValue right) => left.Equals(right);

    public static bool operator !=(QvdValue left, QvdValue right) => !left.Equals(right);
}
