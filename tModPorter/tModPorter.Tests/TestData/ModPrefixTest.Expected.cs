using Terraria;
using Terraria.ModLoader;

public class ModPrefixTest : ModPrefix
{

	// public override SetStaticDefaults() => DisplayName.SetDefault("Test");

	void Method() {
		ModPrefix modPrefix = PrefixLoader.GetPrefix(Type);
		modPrefix = PrefixLoader.GetPrefix(Type);
	}
}
