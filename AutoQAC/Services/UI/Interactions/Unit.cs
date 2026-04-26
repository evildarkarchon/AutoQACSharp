namespace AutoQAC.Services.UI.Interactions;

/// <summary>
/// Sentinel type for interactions that carry no meaningful payload. Replaces
/// <c>System.Reactive.Unit</c> for the ViewModel layer, which must not depend
/// on <c>System.Reactive</c>.
/// </summary>
public readonly record struct Unit
{
    public static Unit Default => default;
    public override string ToString() => "()";
}
