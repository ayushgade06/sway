namespace Sway.Core.Input;

public readonly record struct Contact(int Id, bool Tip, bool Confident, float Xmm, float Ymm);

/// <summary>
/// One complete touchpad frame. <see cref="Contacts"/> may be a buffer the source reuses for the next
/// frame: consumers that keep contacts beyond the event must copy them.
/// </summary>
public readonly record struct TouchFrame(long TimestampTicks, IReadOnlyList<Contact> Contacts, float WidthMm, float HeightMm);

public interface ITouchSource : IDisposable
{
    /// <summary>Raised on the input thread.</summary>
    event Action<TouchFrame> Frame;
}
