using Verse;

namespace FactionColonies.VPE
{
    /// <summary>
    /// Registers the VPE ability provider when this compat assembly loads. The assembly itself is only
    /// loaded when Vanilla Psycasts Expanded is active (gated via LoadFolders), so registration is
    /// unconditional here. Providers are app-lifetime singletons; see <see cref="AbilitySystemRegistry"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class VPECompatInit
    {
        static VPECompatInit()
        {
            AbilitySystemRegistry.Register(new VPEAbilityProvider());
            LogUtil.MessageForce("VPE patched");
        }
    }
}
