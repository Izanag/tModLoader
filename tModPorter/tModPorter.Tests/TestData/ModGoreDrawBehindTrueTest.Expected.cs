using Terraria.ID;
using Terraria.ModLoader;

public class ModGoreDrawBehindTrueTest : ModGore
{
	public override void SetStaticDefaults()
	{
		GoreID.Sets.DrawBehind[Type] = true;
	}
}
