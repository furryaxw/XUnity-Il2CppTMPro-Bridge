using XUnity.Il2CppTMProBridge.Core;

namespace XUnity.Il2CppTMProBridge.Tests;

public sealed class ReentrancyGuardTests
{
    [Fact]
    public void TryEnter_BlocksSameKeyUntilLeaseIsDisposed()
    {
        using var guard = new ReentrancyGuard<nint>();
        Assert.True(guard.TryEnter(42, out var first));
        Assert.False(guard.TryEnter(42, out var nested));
        Assert.Null(nested);

        first!.Dispose();
        Assert.True(guard.TryEnter(42, out var after));
        after!.Dispose();
    }

    [Fact]
    public void TryEnter_AllowsDifferentComponents()
    {
        using var guard = new ReentrancyGuard<nint>();
        Assert.True(guard.TryEnter(1, out var first));
        Assert.True(guard.TryEnter(2, out var second));
        second!.Dispose();
        first!.Dispose();
    }
}
