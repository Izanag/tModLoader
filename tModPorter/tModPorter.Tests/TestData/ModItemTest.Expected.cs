using Microsoft.Xna.Framework;
using System;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

public class ModItemTest : ModItem
{
	public void IdentifierTest() {
		Console.Write(Mod);
		Item.SetDefaults(0);
		Console.Write(Item);
		Item.accessory = true;
		Console.Write(Item.accessory);
		Item.useTime += 2;

#if COMPILE_ERROR
		Console.WriteLine(Item.IsCandidateForReforge/* tModPorter Note: Removed. Use `maxStack == 1 || Item.AllowReforgeForStackableItem` or `Item.Prefix(-3)` to check whether an item is reforgeable */);
		Item.CloneWithModdedDataFrom(Item)/* tModPorter Note: Removed. Use Clone, ResetPrefix or Refresh */;
#endif

		Item.ResearchUnlockCount = 1;
	}

	public override void SetStaticDefaults()
	{
		/* Tooltip.SetDefault(
			"This tooltip\n" +
			"Has multiple lines"); */
		Terraria.ID.AmmoID.Sets.IsSpecialist[Type] = true;
	}

#if COMPILE_ERROR
	public override bool IgnoreDamageModifiers/* tModPorter Note: Removed. If you returned true, consider leaving Item.DamageType as DamageClass.Default, or make a custom DamageClass which returns StatInheritanceData.None in GetModifierInheritance */ => false;

	public override bool OnlyShootOnSwing/* tModPorter Note: Removed. If you returned true, set Item.useTime to a multiple of Item.useAnimation */ => false;
#endif

	protected override bool CloneNewInstances => false;

	public override ModItem Clone(Item newEntity) { return null; }

	public override bool CanReforge()/* tModPorter Note: Use CanReforge instead for logic determining if a reforge can happen. */ { return false; /* comment */ }

	public override Nullable<bool> UseItem(Player player)/* tModPorter Suggestion: Return null instead of false */ { return true; /* comment */ }

	public override void HoldStyle(Player player, Rectangle heldItemFrame) { /* comment */ }

	public override void UseStyle(Player player, Rectangle heldItemFrame) { /* comment */ }

	public override bool CanEquipAccessory(Player player, int slot, bool modded)/* tModPorter Suggestion: Consider using new hook CanAccessoryBeEquippedWith */ { return true; /* comment */ }

	public override void NetReceive(BinaryReader reader) { /* Empty */ }

	public override void ModifyWeaponKnockback(Player player, ref StatModifier knockback) { /* Empty */ }

	public override void ModifyWeaponCrit(Player player, ref float crit) { /* Empty */ }

	public override void ModifyWeaponDamage(Player player, ref StatModifier damage) {
		damage += 0.1f;
		damage *= 0.2f;
		damage.Flat += 4;
	}

	public override void OnCreated(ItemCreationContext context) {
		if (context is RecipeItemCreationContext) { }
		else if (context is InitializationItemCreationContext) { }
	}

	public override void LoadData(TagCompound tag) { /* Empty */ }

	public override void SaveData(TagCompound tag)/* tModPorter Suggestion: Edit tag parameter instead of returning new TagCompound */ {}

	public override void ExtractinatorUse(int extractinatorBlockType, ref int resultType, ref int resultStack) { /* Empty */ }

	public override void ModifyHitNPC(Player player, NPC target, ref NPC.HitModifiers modifiers) { }
	public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damage) { }
	public override void ModifyHitPvp(Player player, Player target, ref Player.HurtModifiers modifiers) { }
	public override void OnHitPvp(Player player, Player target, Player.HurtInfo hurtInfo) { }

#if COMPILE_ERROR
	public override void AutoLightSelect(ref bool dryTorch, ref bool wetTorch, ref bool glowstick)/* tModPorter Note: Removed. Use ItemID.Sets.Torches[Type], ItemID.Sets.WaterTorches[Type], and ItemID.Sets.Glowsticks[Type] in SetStaticDefaults */
	{
		dryTorch = true;
	}
#endif
}
