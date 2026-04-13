using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class GlobalProjectileTest : GlobalProjectile
{
	public override Nullable<bool> CanDamage(Projectile projectile)/* tModPorter Suggestion: Return null instead of true */ { return false; }

	public override bool TileCollideStyle(Projectile projectile, ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac) => true;

	public override bool PreDrawExtras(Projectile projectile, Player player)/* tModPorter Replace 'Main.player[Projectile.owner]' with 'player'. */ { return true; }

	public override bool PreDraw(Projectile projectile, Player player, ref Color lightColor)/* tModPorter Replace 'Main.player[Projectile.owner]' with 'player'. */ { return true; }

	public override void PostDraw(Projectile projectile, Player player, Color lightColor)/* tModPorter Replace 'Main.player[Projectile.owner]' with 'player'. */ { /* Empty */ }

#if COMPILE_ERROR
	public override void DrawBehind(Projectile projectile, int index, List<int> behindNPCsAndTiles, List<int> behindNPCs, List<int> behindProjectiles, List<int> overPlayers, List<int> overWiresUI)/* tModPorter Note: Removed. Set Projectile.drawLayer instead */ {
		projectile.drawLayer=ProjectileDrawLayerID.BehindNPCsAndTiles;
		projectile.drawLayer=ProjectileDrawLayerID.BehindNPCs;
		projectile.drawLayer=ProjectileDrawLayerID.BehindProjectiles;
		projectile.drawLayer=ProjectileDrawLayerID.OverWiresUI;
	}
#endif

#if COMPILE_ERROR
	public override bool? SingleGrappleHook(int type, Player player)/* tModPorter Note: Removed. In SetStaticDefaults, use ProjectileID.Sets.SingleGrappleHook[type] = true if you previously had this method return true */ { return null; }
#endif

#if COMPILE_ERROR // duplicate method
	public override void ModifyHitNPC(Projectile projectile, NPC target, ref NPC.HitModifiers modifiers) { }
#endif
	public override void ModifyHitNPC(Projectile projectile, NPC target, ref NPC.HitModifiers modifiers) { }
	public override void OnHitNPC(Projectile projectile, NPC target, NPC.HitInfo hit, int damage) { }
	public override void ModifyHitPlayer(Projectile projectile, Player target, ref Player.HurtModifiers modifiers) { }
	public override void OnHitPlayer(Projectile projectile, Player target, Player.HurtInfo info) { }
	public override void OnKill(Projectile projectile, int timeLeft) { }
}
