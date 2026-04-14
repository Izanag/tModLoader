using Terraria;
using Terraria.ModLoader;

public class ModPylonTest : ModPylon
{
	public override NPCShop.Entry GetNPCShopEntry()/* tModPorter See ExamplePylonTile for an example. To register to specific NPC shops, use the new shop system directly in ModNPC.AddShop, GlobalNPC.ModifyShop or ModSystem.PostAddRecipes */
{return base.GetNPCShopEntry();}
}
