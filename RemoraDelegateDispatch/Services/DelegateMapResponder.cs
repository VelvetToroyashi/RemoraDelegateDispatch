using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Options;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.Gateway.Responders;
using Remora.Results;

namespace RemoraDelegateDispatch.Services;

public delegate ValueTask<Result> ResponderDelegate(IGatewayEvent @event, IServiceProvider services, CancellationToken cancellationToken);

public class DelegateMapResponder(IOptions<DelegateMapBuilder> _mapBuilder, IServiceProvider _services) : IResponder<IGatewayEvent>
{
    private static readonly MethodInfo ValueTaskFromResult = typeof(ValueTask).GetMethod(nameof(ValueTask.FromResult), BindingFlags.Public | BindingFlags.Static)
                                                                              !.MakeGenericMethod(typeof(IResult));
    private static readonly MethodInfo ResultFromSuccess = typeof(Result).GetMethod(nameof(Result.FromSuccess), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo GetServiceMethodInfo = typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService), BindingFlags.Instance | BindingFlags.Public)!;
    
    private readonly FrozenDictionary<Type, ResponderDelegate[]> _map = BuildMap(_mapBuilder);

    private static FrozenDictionary<Type, ResponderDelegate[]> BuildMap(IOptions<DelegateMapBuilder> mapBuilder)
    {
        var tempDictionary = new Dictionary<Type, ResponderDelegate[]>();
        foreach (var (responderType, responderList) in mapBuilder.Value._responders)
        {
            var responderDelegates = responderList.Select(del => BuildDelegate(responderType, del)).ToArray();
            tempDictionary[responderType] = responderDelegates;
        }

        return tempDictionary.ToFrozenDictionary();
    }

    /// <inheritdoc/>
    public async Task<Result> RespondAsync(IGatewayEvent gatewayEvent, CancellationToken ct = default)
    {
        var hasResponders = _map.TryGetValue(gatewayEvent.GetType(), out var delegates);

        if (!hasResponders)
        {
            return Result.FromSuccess();
        }
        
        var errors = new List<IResult>();

        foreach (var responder in delegates!)
        {
            var delegateResult = await responder(gatewayEvent, _services, ct);

            if (!delegateResult.IsSuccess)
            {
                errors.Add(delegateResult);
            }
        }

        return errors.Count > 0 ? new AggregateError(errors) : Result.FromSuccess();
    }

    private static ResponderDelegate BuildDelegate(Type inputType, Delegate invocation)
    {
        var eventParam = Expression.Parameter(inputType, "event");
        var serviceProvider = Expression.Parameter(typeof(IServiceProvider), "services");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "ct");

        var invokeArguments = invocation.Method.GetParameters();
        var arguments = new Expression[invokeArguments.Length];
        var lastArgumentIsCt = false;

        arguments[0] = eventParam;

        if (invokeArguments[^1].ParameterType == typeof(CancellationToken))
        {
            arguments[^1] = cancellationToken;
            lastArgumentIsCt = true;
        }

        if (arguments.Length > 1)
        {
            for (var i = 1; i < arguments.Length - (lastArgumentIsCt ? 2 : 1); i++)
            {
                arguments[i] = Expression.Convert(Expression.Call(serviceProvider, GetServiceMethodInfo), invokeArguments[i].ParameterType);
            }
        }

        var call = CoerceToValueTask(Expression.Call(Expression.Constant(invocation.Target), invocation.Method));

        var compiled = Expression.Lambda<ResponderDelegate>(call).Compile();

        return compiled;
    }
    
    /// <summary>
    /// Coerces the static result type of an expression to a <see cref="ValueTask{IResult}"/>.
    /// <list type="bullet">
    /// <item>If the type is <see cref="ValueTask{T}"/>, returns the expression as-is</item>
    /// <item>If the type is <see cref="Task{T}"/>, returns an expression wrapping the Task in a <see cref="ValueTask{IResult}"/></item>
    /// <item>Otherwise, throws <see cref="InvalidOperationException"/></item>
    /// </list>
    /// </summary>
    /// <param name="expression">The input expression.</param>
    /// <returns>The new expression.</returns>
    /// <exception cref="InvalidOperationException">If the type of <paramref name="expression"/> is not wrappable.</exception>
    public static Expression CoerceToValueTask(Expression expression)
    {
        var expressionType = expression.Type;

        MethodCallExpression invokerExpr;
        if (expressionType.IsConstructedGenericType && expressionType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            invokerExpr = Expression.Call(ToResultValueTaskInfo.MakeGenericMethod(expressionType.GetGenericArguments()[0]), expression);
        }
        else if (expressionType.IsConstructedGenericType && expressionType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            invokerExpr = Expression.Call(ToResultTaskInfo.MakeGenericMethod(expressionType.GetGenericArguments()[0]), expression);
        }
        else if (expressionType == typeof(void))
        {
            invokerExpr = Expression.Call(ValueTaskFromResult, Expression.Block(expression, Expression.Convert(Expression.Call(ResultFromSuccess), typeof(IResult))));
        }
        else
        {
            throw new InvalidOperationException($"{nameof(CoerceToValueTask)} expression must be void, {nameof(Task<IResult>)}, or {nameof(ValueTask<IResult>)}");
        }

        return invokerExpr;
    }

    private static async ValueTask<IResult> ToResultValueTask<T>(ValueTask<T> task) where T : IResult
        => await task;

    private static async ValueTask<IResult> ToResultTask<T>(Task<T> task) where T : IResult
        => await task;

    private static readonly MethodInfo ToResultValueTaskInfo
        = typeof(DelegateMapBuilder).GetMethod(nameof(ToResultValueTask), BindingFlags.Static | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException($"Did not find {nameof(ToResultValueTask)}");

    private static readonly MethodInfo ToResultTaskInfo
        = typeof(DelegateMapBuilder).GetMethod(nameof(ToResultTask), BindingFlags.Static | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException($"Did not find {nameof(ToResultTask)}");
}
