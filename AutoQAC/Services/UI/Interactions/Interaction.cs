using System;
using System.Threading.Tasks;

namespace AutoQAC.Services.UI.Interactions;

/// <summary>
/// In-house dialog interaction abstraction. A ViewModel raises an interaction by
/// calling <see cref="Handle"/>; a View code-behind registers a single handler
/// via <see cref="RegisterHandler"/> and stores the returned <see cref="IDisposable"/>
/// for the lifetime of the View.
/// </summary>
public sealed class Interaction<TInput, TOutput>
{
    private Func<TInput, Task<TOutput>>? _handler;

    /// <summary>
    /// Registers the handler that will be invoked by <see cref="Handle"/>.
    /// Throws if a handler is already registered. Disposing the returned token
    /// clears the handler and allows a new registration.
    /// </summary>
    public IDisposable RegisterHandler(Func<TInput, Task<TOutput>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handler is not null)
        {
            throw new InvalidOperationException(
                $"A handler is already registered for Interaction<{typeof(TInput).Name}, {typeof(TOutput).Name}>.");
        }

        _handler = handler;
        return new AnonymousDisposable(() =>
        {
            if (ReferenceEquals(_handler, handler))
            {
                _handler = null;
            }
        });
    }

    /// <summary>
    /// Invokes the registered handler with <paramref name="input"/> and awaits its
    /// reply. Throws if no handler is registered.
    /// </summary>
    public Task<TOutput> Handle(TInput input)
    {
        if (_handler is null)
        {
            throw new InvalidOperationException(
                $"No handler registered for Interaction<{typeof(TInput).Name}, {typeof(TOutput).Name}>.");
        }

        return _handler(input);
    }

    private sealed class AnonymousDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            var dispose = _dispose;
            _dispose = null;
            dispose?.Invoke();
        }
    }
}
