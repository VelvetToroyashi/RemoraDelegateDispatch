using System.Reflection;
using System.Runtime.CompilerServices;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Results;

[assembly: InternalsVisibleTo("RemoraDelegateDispatch.Tests")]
namespace RemoraDelegateDispatch.Services;

/// <summary>
/// Wrapper/helper type for building a map of delegates, and creating responders based upon that.
/// </summary>
public class DelegateMapBuilder
{
    internal readonly List<(Type, Delegate)> _responders = new();

    /// <summary>
    /// Subscribes a delegate to a given event.
    /// </summary>
    /// <param name="del"></param>
    /// <typeparam name="TEvent"></typeparam>
    public void AddResponderAsync<TEvent>(Delegate del) where TEvent : IGatewayEvent
    {
        ValidateDelegate<TEvent>(del);
        _responders.Add((typeof(TEvent), del));
    }

    private static void ValidateDelegate<TEvent>(Delegate del)
    {
        var parameters = del.Method.GetParameters();
        var returnType = del.Method.ReturnType;

        if (parameters.Length < 1)
        {
            throw new InvalidOperationException("Cannot register delegate responder: The delegate must at least take the event as a parameter.");
        }

        if (parameters[0].ParameterType != typeof(TEvent))
        {
            throw new InvalidOperationException("The delegate's first parameter must match the event it is subscribing to.");
        }

        if (!CanCoalesceToResult(returnType))
        {
            throw new InvalidOperationException
            (
                "The delegate's return type cannot be converted to a result. Acceptable return values include Tasks and ValueTasks optionally returning a generic or non-generic Result and void."
            );
        }
    }

    private static bool CanCoalesceToResult(Type type) 
        => type == typeof(void)      ||
           type == typeof(Task)      ||
           type == typeof(ValueTask) ||
           type.IsAssignableTo(typeof(IResult)) ||
           type.GetMethod(nameof(Task.GetAwaiter), BindingFlags.Public | BindingFlags.Instance) is not null && 
           type.GetGenericArguments() is [var genericType] _ &&
           genericType.IsAssignableTo(typeof(IResult));

}