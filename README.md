# Remora Delegate Dispatch

---
# What is this library?

This library aims to simplify the process of registering simple responders. </br>
It is very common to have simple responders that have limited functionality, sometimes even just a single line of code.

One such example may look like this (C# 12):
```csharp
public class MyResponder(MessageHandlerService messages) : IResponder<IMessageCreate> 
{
    public Task<Result> RespondAsync(IMessageCreate gatewayEvent, CancellationToken ct = default)
        => messages.HandleAsync(gatewayEvent, ct);
}
```

Shims, ad-hoc, stubs, whatever you wish to call them, this class's only purpose is to pump events to a service.

This requires having a class (and commonly, an extra file) just to provide this functionality. </br>
Instead, this library offers an API that allows registering delegates to respond to events instead of full-fledged classes.

---

# Setup and Use

As a prerequisite, you will need to install the library, either as a project reference, or from NuGet. </br>
Setting this library up only requires a single line of code. On a service collection, call the following method:

```csharp
serviceCollection.AddDelegateResponders();
```

This call **MUST** be made before calls to `.AddDelegateResponder<TEvent>`. </br>
Here's how you'd add a responder:

```csharp
serviceCollection.AddDelegateResponder<IMessageCreate>
(
    async (IMessageCreate message, CancellationToken ct) 
    {
        // Logic here
    }
);
```

The library automatically coerces the following delegate types into the appropriate response type for Remora:

- Task (`async (TEvent t) => {}`)
- Void (`(TEvent t) => {}`)
- Result (`(TEvent t) => Result.FromSuccess()`)
- Task<Result> (and any result-like type (`async (TEvent t) => Result.FromSuccess()`))
- ValueTask (Pass a **static** method, which is implicitly converted to a method group delegate)
- ValueTask<Result> (same as above).

---

You can also accept services in your delegate, similar to ASP.NET. The services MUST be:

- After the event parameter
- Before the `CancellationToken` parameter (if present)
- Registered with the same DI container the delegate is registered in
- Resolvable from a DI scope

This is a valid delegate (assuming the service exists, for sake of example):

```csharp
serviceCollection.AddDelegateResponders();
serviceCollection.AddDelegateResponder<IMessageCreate>
(
    (IMessageCreate message, MessageHandlerService service, CancellationToken ct)
        => service.HandleAsync(message, ct);
);
```
> [!IMPORTANT]
> ## Delegates *MUST* be registered before the container is built. 
> The dispatcher is considered immutable after that point.

---

# Known issues and considerations

There are a few outstanding issues in the library which stem from difficult decision making, or limitations of the C# type system itself.

### Problem: Delegates are executed sequentially
Potential Solution: The C# STD library does not offer a method for awaiting `ValueTask`s in parallel in the same manner as it does for its
reference-type based counterpart (`Task`). Unfortunately this means that each delegate currently has to be `await`ed in order, which causes its own set of problems.

One solution is to potentially check for completion in a loop as the Task methods do, but this likely has hidden implications, given that
the BCL has not implemented something similar.

### Exceptions break dispatch
Potential solution: As it stands right now, exceptions bubble up through the callstack, breaking the iterative execution of these delegates.
This is simply solved by adding a catch statement, and packing these exceptions into an `ExceptionError` to be handled like any other. This should be fixed soon.
