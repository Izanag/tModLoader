using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class GlobalNPCTest : GlobalNPC
{
	public override bool PreKill(NPC npc) { return true; /* Empty */ }

	public override void OnKill(NPC npc) { /* Empty */ }

	public override bool SpecialOnKill(NPC npc) { return true; /* Empty */ }

	public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
		spriteBatch.Draw(null, npc.Center - screenPos, drawColor);
		return true;
	}

	public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
		spriteBatch.Draw(null, npc.Center - screenPos, drawColor);
	}

	public override bool CanHitNPC(NPC npc, NPC target)/* tModPorter Suggestion: Return true instead of null */ {
		return true;
	}

	public override void ApplyDifficultyAndPlayerScaling(NPC npc, int numPlayers, float balance, float bossLifeScale)/* tModPorter Note: bossLifeScale -> balance (bossAdjustment is different, see the docs for details) */
	{
	}

	public override void ModifyActiveShop(NPC npc, string shopName, Item[] items) { /* Empty */ }

	public override void HitEffect(NPC npc, NPC.HitInfo hit) { }
	public override void ModifyHitPlayer(NPC npc, Player target, ref Player.HurtModifiers modifiers) { }
	public override void OnHitPlayer(NPC npc, Player target, Player.HurtInfo hurtInfo) { }
	public override void ModifyHitNPC(NPC npc, NPC target, ref NPC.HitModifiers modifiers) { }
	public override void OnHitNPC(NPC npc, NPC target, NPC.HitInfo hit) { }
	public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers) { }
	public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damage) { }
	public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers) { }
	public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damage) { }
	public override void ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers) {}
	public override bool ModifyCollisionData(NPC npc, Rectangle victimHitbox, ref int immunityCooldownSlot, ref MultipliableFloat damageMultiplier, ref Rectangle npcHitbox) => false;
	public override void DrawTownAttackSwing(NPC npc, ref Texture2D item, ref Rectangle itemFrame, ref int itemSize, ref float scale, ref Vector2 offset) { }
	public override void DrawTownAttackGun(NPC npc, ref Texture2D item, ref Rectangle itemFrame, ref float scale, ref int horizontalHoldoutOffset)/* tModPorter Note: closeness is now horizontalHoldoutOffset, use 'horizontalHoldoutOffset = Main.DrawPlayerItemPos(1f, itemtype) - originalClosenessValue' to adjust to the change. See docs for how to use hook with an item type. */ {
		horizontalHoldoutOffset = 20;
	}

	public override void EditSpawnPool(IDictionary<int, float> pool, NPC.Spawner spawner) {
		if (spawner.waterTile) { }
	}
}
