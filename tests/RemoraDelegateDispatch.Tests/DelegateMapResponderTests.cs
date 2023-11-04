using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.Gateway.Extensions;
using Remora.Rest;
using Remora.Results;
using RemoraDelegateDispatch.Extensions;
using RemoraDelegateDispatch.Services;

namespace RemoraDelegateDispatch.Tests;

public class DelegateMapResponderTests
{
   [Test]
   public async Task DispatchesSimpleDelegateCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      var isSuccess = false;

      services.AddDelegateResponder<IMessageCreate>
      (
         async (IMessageCreate _) =>
         {
            isSuccess = true;
            return Result.FromSuccess();
         }
      );

      var provider = services.BuildServiceProvider();
      var service = provider.GetRequiredService<DelegateMapResponder>();

      await service.RespondAsync(Substitute.For<IMessageCreate>());

      Assert.That(isSuccess);
   }
   
   [Test]
   public async Task DispatchesDelegateWithCancellationTokenCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      var isSuccess = false;
      var ct = new CancellationToken(true);

      services.AddDelegateResponder<IMessageCreate>
      (
         (IMessageCreate _, CancellationToken ct) =>
         {
            isSuccess = ct.IsCancellationRequested;
         }
      );

      var provider = services.BuildServiceProvider();
      var service = provider.GetRequiredService<DelegateMapResponder>();

      await service.RespondAsync(Substitute.For<IMessageCreate>(), ct);

      Assert.That(isSuccess);
   }
   
   [Test]
   public async Task DispatchesDelegateWithServiceCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      services.AddDelegateResponder<IMessageCreate>
      (
         (IMessageCreate _, IRestHttpClient _) => { }
      );

      Assert.DoesNotThrowAsync
      (
         async () =>
         {
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<DelegateMapResponder>();

            await service.RespondAsync(Substitute.For<IMessageCreate>());
         }
      );
   }
   
   [Test]
   public async Task CoercesVoidDelegateCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      services.AddDelegateResponder<IMessageCreate>
      (
         (IMessageCreate _) => { } // Action<IMessageCreate>
      );

      Assert.DoesNotThrowAsync
      (
         async () =>
         {
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<DelegateMapResponder>();

            await service.RespondAsync(Substitute.For<IMessageCreate>());
         }
      );
   }
   
   [Test]
   public async Task CoercesTaskDelegateCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      services.AddDelegateResponder<IMessageCreate>
      (
         async (IMessageCreate _) => { } // Func<IMessageCreate, Task>
      );

      Assert.DoesNotThrowAsync
      (
         async () =>
         {
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<DelegateMapResponder>();

            await service.RespondAsync(Substitute.For<IMessageCreate>());
         }
      );
   }
   
   [Test]
   public async Task CoercesValueTaskDelegateCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      services.AddDelegateResponder<IMessageCreate>
      (
         ValueTaskMethod // Func<IMessageCreate, ValueTask>
      );
      
      static ValueTask ValueTaskMethod(IMessageCreate _) => ValueTask.CompletedTask;

      Assert.DoesNotThrowAsync
      (
         async () =>
         {
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<DelegateMapResponder>();

            await service.RespondAsync(Substitute.For<IMessageCreate>());
         }
      );
   }
   
   [Test]
   public async Task CoercesResultTaskDelegateCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      services.AddDelegateResponder<IMessageCreate>
      (
         async (IMessageCreate _) => Result.FromSuccess() // Func<IMessageCreate, Task<Result>>
      );

      Assert.DoesNotThrowAsync
      (
         async () =>
         {
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<DelegateMapResponder>();

            await service.RespondAsync(Substitute.For<IMessageCreate>());
         }
      );
   }
   
   [Test]
   public async Task HandlesComplexDelegateReturnCorrectly()
   {
      var services = new ServiceCollection();
      services.AddDiscordGateway(_ => "");
      services.AddDelegateResponders();

      services.AddDelegateResponder<IMessageCreate>
      (
         async (IMessageCreate _) => Result<int>.FromSuccess(69) // Func<IMessage, Task<Result<T>>>
      );

      Assert.DoesNotThrowAsync
      (
         async () =>
         {
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<DelegateMapResponder>();

            await service.RespondAsync(Substitute.For<IMessageCreate>());
         }
      );
   }
}