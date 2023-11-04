using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.Gateway.Responders;
using Remora.Results;

namespace RemoraDelegateDispatch.Services;

public delegate ValueTask<IResult> ResponderDelegate(IGatewayEvent @event, IServiceProvider services, CancellationToken cancellationToken);

public class DelegateMapResponder(IOptions<DelegateMapBuilder> _mapBuilder, IServiceProvider _services) : IResponder<IGatewayEvent>
{
    private static readonly MethodInfo ValueTaskFromResult = typeof(ValueTask).GetMethod(nameof(ValueTask.FromResult), BindingFlags.Public | BindingFlags.Static)
                                                                              !.MakeGenericMethod(typeof(IResult));
    private static readonly MethodInfo ResultFromSuccess = typeof(Result).GetMethod(nameof(Result.FromSuccess), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo GetServiceMethodInfo = typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService), BindingFlags.Instance | BindingFlags.Public)!;
    
    private readonly ResponderDelegate[] _responders = BuildMap(_mapBuilder);

    private static ResponderDelegate[] BuildMap(IOptions<DelegateMapBuilder> mapBuilder)
    {
        var compiledArray = new ResponderDelegate[mapBuilder.Value._responders.Count];
        var tempArray = mapBuilder.Value._responders.ToArray();

        for (var i = 0; i < tempArray.Length; i++)
        {
            var (responderType, responderDelegate) = tempArray[i];
            compiledArray[i] = BuildDelegate(responderType, responderDelegate);
        }

        return compiledArray;
    }

    /// <inheritdoc/>
    public async Task<Result> RespondAsync(IGatewayEvent gatewayEvent, CancellationToken ct = default)
    {
        var errors = new List<IResult>();

        foreach (var responder in _responders)
        {
            var delegateResult = responder(gatewayEvent, _services, ct);

            if (delegateResult.IsCompletedSuccessfully)
            {
                if (delegateResult.Result is not { IsSuccess: false } unsuccessfulResult)
                {
                    continue;
                }
                
                errors.Add(unsuccessfulResult);

                continue;
            }

            var delegateResultResult = await delegateResult;
            if (!delegateResultResult.IsSuccess)
            {
                errors.Add(delegateResultResult);
            }
        }

        return errors.Count > 0 ? new AggregateError(errors) : Result.FromSuccess();
    }

    private static ResponderDelegate BuildDelegate(Type inputType, Delegate invocation)
    {
        var eventParam = Expression.Parameter(typeof(IGatewayEvent), "event");
        var serviceProvider = Expression.Parameter(typeof(IServiceProvider), "services");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "ct");

        var invokeArguments = invocation.Method.GetParameters();
        var arguments = new Expression[invokeArguments.Length];
        var lastArgumentIsCt = false;
        
        arguments[0] = Expression.TypeAs(eventParam, inputType);

        if (invokeArguments[^1].ParameterType == typeof(CancellationToken))
        {
            arguments[^1] = cancellationToken;
            lastArgumentIsCt = true;
        }

        if (arguments.Length > 1)
        {
            for (var i = 1; i < arguments.Length - (lastArgumentIsCt ? 1 : 0); i++)
            {
                var parameterType = invokeArguments[i].ParameterType;
                arguments[i] = Expression.Convert(Expression.Call(serviceProvider, GetServiceMethodInfo, Expression.Constant(parameterType)), parameterType);
            }
        }

        var check = Expression.NotEqual(arguments[0], Expression.Constant(null));
        
        var call = CoerceToValueTask(Expression.Call(invocation.Target is null ? null : Expression.Constant(invocation.Target), invocation.Method, arguments));
        var completedResultValueTask = Expression.Call(ValueTaskFromResult, Expression.Convert(Expression.Call(ResultFromSuccess), typeof(IResult)));
        
        var retBlock = Expression.Condition(check, call, completedResultValueTask);
        var compiled = Expression.Lambda<ResponderDelegate>(retBlock, eventParam, serviceProvider, cancellationToken).Compile();

        return compiled; //Unsafe.As<ResponderDelegate>(compiled);
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
        else if (expressionType == typeof(void) || expressionType == typeof(Task) || expressionType == typeof(ValueTask))
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
        = typeof(DelegateMapResponder).GetMethod(nameof(ToResultValueTask), BindingFlags.Static | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException($"Did not find {nameof(ToResultValueTask)}");

    private static readonly MethodInfo ToResultTaskInfo
        = typeof(DelegateMapResponder).GetMethod(nameof(ToResultTask), BindingFlags.Static | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException($"Did not find {nameof(ToResultTask)}");
}
