using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Gateway.Services;
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
         (IMessageCreate _) =>
         {
            isSuccess = true;
         }
      );

      var provider = services.BuildServiceProvider();
      var service = provider.GetRequiredService<DelegateMapResponder>();

      await service.RespondAsync(Substitute.For<IMessageCreate>());

      Assert.That(isSuccess);
   }
}