using Terraria.ModLoader;
using Terraria.ID;

public class ModBuffTest : ModBuff
{
	public override void SetStaticDefaults() {
		BuffID.Sets.NurseCannotRemoveDebuff[Type] = true;
		BuffID.Sets.BuffTimeIsExtendedWithGameDifficulty[Type] = true;

		bool a = !BuffID.Sets.NurseCannotRemoveDebuff[0];

		BuffID.Sets.IsATagBuff[Type] = true;

		BuffID.Sets.MountType[Type] = ModContent.MountType<ExampleMinecartMount>();
	}

	public override void ModifyBuffText(ref string buffName, ref string tip, ref int rare) { }
}

public class ExampleMinecartMount : ModMount
{
}
