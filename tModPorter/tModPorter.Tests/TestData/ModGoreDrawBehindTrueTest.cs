using Terraria.ModLoader;

public class ModGoreDrawBehindTrueTest : ModGore
{
	public override bool DrawBehind(Terraria.Gore gore) { return true; }
}
