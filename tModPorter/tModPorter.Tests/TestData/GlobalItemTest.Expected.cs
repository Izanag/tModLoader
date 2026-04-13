using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

public class GlobalItemTest : GlobalItem
{
	protected override bool CloneNewInstances => true;

	public override Nullable<bool> UseItem(Item item, Player player)/* tModPorter Suggestion: Return null instead of false */ => null;

	public override bool CanReforge(Item item)/* tModPorter Note: Use CanReforge instead for logic determining if a reforge can happen. */ { return false; /* comment */ }

	public override void HoldStyle(Item item, Player player, Rectangle heldItemFrame) { /* comment */ }

	public override void UseStyle(Item item, Player player, Rectangle heldItemFrame) { /* comment */ }

	public override bool CanEquipAccessory(Item item, Player player, int slot, bool modded)/* tModPorter Suggestion: Consider using new hook CanAccessoryBeEquippedWith */ { return true; /* comment */ }

	public override void ModifyWeaponKnockback(Item item, Player player, ref StatModifier knockback) { /* Empty */ }

	public override void ModifyWeaponCrit(Item item, Player player, ref float crit) { /* Empty */ }

	public override void ModifyWeaponDamage(Item item, Player player, ref StatModifier damage) {
		damage += 0.1f;
		damage *= 0.2f;
		damage.Flat += 4;
	}

	public override void OnCreated(Item item, ItemCreationContext context) { }

	public override void LoadData(Item item, TagCompound tag) { /* Empty */ }

	public override void SaveData(Item item, TagCompound tag)/* tModPorter Suggestion: Edit tag parameter instead of returning new TagCompound */ {}

	public override void ExtractinatorUse(int extractType, int extractinatorBlockType, ref int resultType, ref int resultStack) { /* Empty */ }

	public override void ModifyHitNPC(Item item, Player player, NPC target, ref NPC.HitModifiers modifiers) { }
	public override void OnHitNPC(Item item, Player player, NPC target, NPC.HitInfo hit, int damage) { }
	public override void ModifyHitPvp(Item item, Player player, Player target, ref Player.HurtModifiers modifiers) { }
	public override void OnHitPvp(Item item, Player player, Player target, Player.HurtInfo hurtInfo) { }
}
