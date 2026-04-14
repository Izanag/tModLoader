using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.ModLoader;

public class ModBossBarTest : ModBossBar
{
	public override Nullable<bool> ModifyInfo(ref BigProgressBarInfo info, ref float life, ref float lifeMax, ref float lifePercent, ref float shieldPercent)/* tModPorter Note: life and shield current and max values are now separate to allow for hp/shield number text draw */ => null;

	public override bool PreDraw(SpriteBatch spriteBatch, NPC npc, ref BossBarDrawParams drawParams) {
		float lifePercent = drawParams.Life / drawParams.LifeMax;
		float shieldPercent = drawParams.Shield / drawParams.ShieldMax;
		return false;
	}
}
