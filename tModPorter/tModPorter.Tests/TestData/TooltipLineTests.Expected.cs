using Terraria.ModLoader; 

public class TooltipLineTests
{
	void Method() {
		TooltipLine line = new TooltipLine(null, "", "");
		string mod = line.Mod;
		line.Text = "";
		;/* tModPorter Note: Removed. Use OverrideColor or other tooltip styling logic instead. */
		;/* tModPorter Note: Removed. Use OverrideColor or other tooltip styling logic instead. */
		line.OverrideColor = null;

		line = new TooltipLine(null, "", "") {
			Text = "",			OverrideColor = null
		};
	}
}
