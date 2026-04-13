using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

public class SingleGrappleHookModProjectileTest : ModProjectile
{
	public override void SetStaticDefaults()
	{
		ProjectileID.Sets.SingleGrappleHook[Type] = true;
	}
}
