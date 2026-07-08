using System;

namespace AutoQAC.Services.UI;

/// <summary>
/// Minimal <see cref="IObserver{T}"/> implementation that calls an action on each
/// pushed value. Lets ViewModels subscribe to <see cref="IObservable{T}"/> streams
/// (e.g. <c>IStateService.StateChanged</c>) without depending on
/// <c>System.Reactive</c> for the <c>Subscribe(Action&lt;T&gt;)</c> overload.
/// </summary>
public sealed class CallbackObserver<T>(Action<T> onNext, Action<Exception>? onError = null) : IObserver<T>
{
    public void OnCompleted()
    {
    }

    public void OnError(Exception error) => onError?.Invoke(error);

    public void OnNext(T value) => onNext(value);
}
