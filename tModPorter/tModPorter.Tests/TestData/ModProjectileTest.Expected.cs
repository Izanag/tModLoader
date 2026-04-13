using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class ModProjectileTest : ModProjectile
{
	public void IdentifierTest() {
		Console.Write(Projectile);
		Console.Write(AIType);
		Console.Write(CooldownSlot);
		Console.Write(DrawOffsetX);
		Console.Write(DrawOriginOffsetY);
		Console.Write(DrawOriginOffsetX);
		;/* tModPorter Note: Removed. Replace with Projectile.drawLayer = ProjectileDrawLayerID.HeldProjOverHand. */
	}

	public override void SetStaticDefaults()
	{
		;/* tModPorter Note: Removed. AI() should use master.RotatedRelativePoint(master.MountedCenter + ...) to position held projectiles. */
		;/* tModPorter Note: Removed. Now true by default. See Projectile.usesOwnerLight and Projectile.drawLayer for more details. */
	}

	public override Nullable<bool> CanDamage()/* tModPorter Suggestion: Return null instead of true */ { return false; }

	public override bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac) { return true; }

	public override bool PreDrawExtras(Player player)/* tModPorter Replace 'Main.player[Projectile.owner]' with 'player'. */ { return true; }

	public override bool PreDraw(Player player, ref Color lightColor)/* tModPorter Replace 'Main.player[Projectile.owner]' with 'player'. */ { return true; }

	public override void PostDraw(Player player, Color lightColor)/* tModPorter Replace 'Main.player[Projectile.owner]' with 'player'. */ { /* Empty */ }

#if COMPILE_ERROR
	public override void DrawBehind(int index, List<int> behindNPCsAndTiles, List<int> behindNPCs, List<int> behindProjectiles, List<int> overPlayers, List<int> overWiresUI)/* tModPorter Note: Removed. Set Projectile.drawLayer instead */ {
		Projectile.drawLayer=ProjectileDrawLayerID.BehindNPCsAndTiles;
		Projectile.drawLayer=ProjectileDrawLayerID.BehindNPCs;
		Projectile.drawLayer=ProjectileDrawLayerID.BehindProjectiles;
		Projectile.drawLayer=ProjectileDrawLayerID.OverWiresUI;
	}
#endif

#if COMPILE_ERROR
	public override bool? SingleGrappleHook(Player player)/* tModPorter Note: Removed. In SetStaticDefaults, use ProjectileID.Sets.SingleGrappleHook[Type] = true if you previously had this method return true */ { return null; }
#endif

#if COMPILE_ERROR // duplicate method
	public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) { }
#endif
	public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) { }
	public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damage) { }
	public override void ModifyHitPlayer(Player target, ref Player.HurtModifiers modifiers) { }
	public override void OnHitPlayer(Player target, Player.HurtInfo info) { }
	public override void OnKill(int timeLeft) { }
}
